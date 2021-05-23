using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Mqtt;
using Signal.Beacon.Core.Network;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.Channel.iRobot
{
    internal class iRobotWorkerService : IWorkerService
    {
        private const string ConfigurationFileName = "iRobot.json";

        private readonly string[] allowedMacCompanies =
        {
            "iRobot Corporation"
        };

        private readonly IConfigurationService configurationService;
        private readonly IHostInfoService hostInfoService;
        private readonly IMacLookupService macLookupService;
        private readonly ILogger<iRobotWorkerService> logger;
        private readonly IMqttClientFactory mqttClientFactory;
        private readonly ICommandHandler<DeviceDiscoveredCommand> discoverCommandHandler;
        private readonly ICommandHandler<DeviceStateSetCommand> deviceStateSetHandler;
        private CancellationToken startCancellationToken;
        private iRobotWorkerServiceConfiguration configuration;
        private readonly Dictionary<string, IMqttClient> roombaClients = new();

        public iRobotWorkerService(
            IConfigurationService configurationService,
            IHostInfoService hostInfoService,
            IMacLookupService macLookupService,
            IMqttClientFactory mqttClientFactory,
            ICommandHandler<DeviceDiscoveredCommand> discoverCommandHandler,
            ICommandHandler<DeviceStateSetCommand> deviceStateSetHandler,
            ILogger<iRobotWorkerService> logger)
        {
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.hostInfoService = hostInfoService ?? throw new ArgumentNullException(nameof(hostInfoService));
            this.macLookupService = macLookupService ?? throw new ArgumentNullException(nameof(macLookupService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.mqttClientFactory = mqttClientFactory ?? throw new ArgumentNullException(nameof(mqttClientFactory));
            this.discoverCommandHandler = discoverCommandHandler ?? throw new ArgumentNullException(nameof(discoverCommandHandler));
            this.deviceStateSetHandler = deviceStateSetHandler ?? throw new ArgumentNullException(nameof(deviceStateSetHandler));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            this.configuration =
                await this.configurationService.LoadAsync<iRobotWorkerServiceConfiguration>(
                    ConfigurationFileName,
                    cancellationToken);

            if (!this.configuration.RoombaRobots.Any())
                _ = this.DiscoverDevicesAsync();
            else this.configuration.RoombaRobots.ForEach((c) => _ = this.ConnectToRoomba(c));
        }

        private async Task ConnectToRoomba(iRobotWorkerServiceConfiguration.RoombaConfiguration config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.RobotId))
                {
                    this.logger.LogWarning("Can't connect to Roomba with unknown RobotIdentifier. Finish connection first.");
                    // TODO: Begin targeted discovery
                    return;
                }

                var client = this.mqttClientFactory.Create();
                client.OnMessage += this.RoombaOnAnyMessage;
                this.roombaClients.Add(config.RobotId, client);

                // Start communication
                await client.StartAsync(
                    config.RobotId,
                    config.IpAddress, 
                    this.startCancellationToken, 
                    8883,
                    config.RobotId, 
                    config.RobotPassword, 
                    true);
                
                // TODO: Rediscover if unable to connect after some period

                // Discover push to Signal
                await this.discoverCommandHandler.HandleAsync(new DeviceDiscoveredCommand(
                    "iRobot Robot", config.RobotId,
                    new List<DeviceEndpoint>
                    {
                        new(iRobotChannels.RoombaChannel, new List<DeviceContact>
                        {
                            new("cycle", "enum", DeviceContactAccess.Get),
                            new("phase", "enum", DeviceContactAccess.Get),
                            new("battery", "double", DeviceContactAccess.Get),
                            new("cleanall", "action", DeviceContactAccess.Write),
                            new("cleanArea", "action", DeviceContactAccess.Write),
                            new("pause", "action", DeviceContactAccess.Write),
                            new("dock", "action", DeviceContactAccess.Write)
                        })
                    }), this.startCancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Roomba connection start failed");
                this.logger.LogWarning("Failed to connect to Roomba.");
            }
        }
        
        private async void RoombaOnAnyMessage(object? _, MqttMessage message)
        {
            if (!message.Topic.Contains("/shadow/update")) return;

            var robotId = message.Topic[12..44];
            var status = JsonSerializer.Deserialize<RoombaMqttStatusDto>(message.Payload);
            if (status?.State?.Reported == null) return;

            // Update battery percentage
            if (status.State.Reported.BatteryPercentage != null)
                await this.PushRoombaStateAsync(robotId, "battery", status.State.Reported.BatteryPercentage);

            if (status.State.Reported.CleanMissionStatus != null)
                await this.UpdateRoombaMissionAsync(robotId, status.State.Reported.CleanMissionStatus);

            if (status.State.Reported.Pose != null)
                this.UpdateRoombaPose(robotId, status.State.Reported.Pose);
        }

        private async Task UpdateRoombaMissionAsync(string robotId, RoombaMqttStatusDto.StateDto.ReportedDto.CleanMissionStatusDto mission)
        {
            await this.PushRoombaStateAsync(robotId, "cycle", mission.Cycle);
            await this.PushRoombaStateAsync(robotId, "phase", mission.Phase);
        }

        private Task PushRoombaStateAsync(string id, string contact, object? value) =>
            this.deviceStateSetHandler.HandleAsync(
                new DeviceStateSetCommand(new DeviceTarget(iRobotChannels.RoombaChannel, id, contact), value),
                this.startCancellationToken);

        private void UpdateRoombaPose(string robotId, RoombaMqttStatusDto.StateDto.ReportedDto.PoseDto pose)
        {
            // TODO: Persist pose
        }

        private Task SendRoombaCommandAsync()
        {
            throw new NotImplementedException();
            //var command = "dock"; // or start
            //var topic = "cmd";
            //var data = new
            //{
            //    command = command,
            //    time = ((DateTime.UtcNow.Ticks - 621355968000000000) / 10000) / 1000 | 0,
            //    initiator = "localApp"
            //};
            //await client.PublishAsync(topic, data);
        }

        private async Task AuthenticateRoombaAsync(string ipAddress, string physicalAddress, string robotId)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync(ipAddress, 8883, this.startCancellationToken);
                var stream = new SslStream(client.GetStream());
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    TargetHost = ipAddress
                }, this.startCancellationToken);

                // Listen forever or timeout
                var startDateTime = DateTime.UtcNow;
                while (!this.startCancellationToken.IsCancellationRequested)
                {
                    // Send magic payload
                    stream.Write(new byte[] { 0xf0, 0x05, 0xef, 0xcc, 0x3b, 0x29, 0x00 });

                    var buffer = new byte[256];
                    var responseLength = 0;
                    do
                    {
                        // Break due to timeout
                        if (DateTime.UtcNow - startDateTime > TimeSpan.FromMinutes(1))
                            break;

                        try
                        {
                            responseLength = await stream.ReadAsync(buffer, this.startCancellationToken);
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogWarning(ex, "Reading Roomba authenticate response failed.");
                            break;
                        }

                        if (responseLength <= 0) 
                            Thread.Sleep(100);
                    } while (responseLength <= 0);

                    // No response after a timeout
                    if (responseLength <= 0)
                    {
                        this.logger.LogDebug("Failed to authenticate Roomba - no response.");
                        break;
                    }

                    this.logger.LogTrace("Received robot password response: {Password}",
                        string.Join(" ", buffer.Take(responseLength).Select(b => $"{b:X2}")));

                    // Handle password response
                    if (responseLength > 7 && buffer[0] == 0xF0)
                    {
                        // Process response
                        var passwordStringUtf8 = Encoding.UTF8.GetString(buffer[07..(responseLength-1)]);
                        this.logger.LogTrace("Received robot password: {Password}", passwordStringUtf8);

                        var config = new iRobotWorkerServiceConfiguration.RoombaConfiguration(
                            ipAddress,
                            physicalAddress,
                            robotId,
                            passwordStringUtf8);

                        // TODO: Save configuration after connected successfully
                        this.configuration.RoombaRobots.Add(config);
                        await this.configurationService.SaveAsync(
                            ConfigurationFileName, 
                            this.configuration,
                            this.startCancellationToken);


                        await this.ConnectToRoomba(config);

                        return;
                    }

                    this.logger.LogInformation("To connect to Roomba, please press HOME button on robot until ring light turns blue...");
                    Thread.Sleep(2000);
                }

                this.logger.LogWarning("Didn't finish Roomba setup - timeout. Try again.");
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to authenticate Roomba.");
                this.logger.LogWarning("Failed to authenticate Roomba.");
            }
        }

        private async Task DiscoverDevicesAsync()
        {
            var ipAddressesInRange = IpHelper.GetIPAddressesInRange(IpHelper.GetLocalIp());
            var matchedHosts =
                await this.hostInfoService.HostsAsync(ipAddressesInRange, new[] {8883}, this.startCancellationToken);
            var hostsWithPort = matchedHosts.Where(mh => mh.OpenPorts.Count() == 1);
            foreach (var hostInfo in hostsWithPort)
            {
                if (string.IsNullOrWhiteSpace(hostInfo.PhysicalAddress))
                {
                    this.logger.LogDebug("Device MAC not found. Ip: {IpAddress}", hostInfo.IpAddress);
                    continue;
                }

                // Validate MAC vendor
                var deviceCompany =
                    await this.macLookupService.CompanyNameLookupAsync(hostInfo.PhysicalAddress, this.startCancellationToken);
                if (!this.allowedMacCompanies.Contains(deviceCompany))
                {
                    this.logger.LogDebug(
                        "Device MAC not whitelisted. Ip: {IpAddress} Mac: {PhysicalAddress} Company: {MacCompany}",
                        hostInfo.PhysicalAddress, hostInfo.IpAddress, deviceCompany ?? "Not found");
                    continue;
                }

                // TODO: Add to possible matches (instead of directly connecting)
                this.logger.LogDebug(
                    "Potential iRobot device found on address \"{DeviceIp}\" (\"{PhysicalAddress}\")",
                    hostInfo.IpAddress, hostInfo.PhysicalAddress);

                // Try to connect
                await this.DiscoverDeviceAsync(hostInfo.IpAddress, hostInfo.PhysicalAddress);
            }
        }

        private async Task DiscoverDeviceAsync(string ipAddress, string physicalAddress)
        {
            try
            {
                var client = new UdpClient();
                var requestData = Encoding.ASCII.GetBytes("irobotmcs");
                client.EnableBroadcast = true;
                await client.SendAsync(requestData, requestData.Length, new IPEndPoint(IPAddress.Parse(ipAddress), 5678));

                while (!this.startCancellationToken.IsCancellationRequested)
                {
                    var serverResponseData = await client.ReceiveAsync();
                    var serverResponse = Encoding.ASCII.GetString(serverResponseData.Buffer);
                    this.logger.LogTrace("Received {Data} from {Source}", serverResponse, serverResponseData.RemoteEndPoint);

                    var response = JsonSerializer.Deserialize<iRobotMcsResponse>(serverResponse);
                    if (response == null ||
                        string.IsNullOrWhiteSpace(response.RobotId))
                        throw new Exception("MCS returned response without robot identifier.");

                    // TODO: Discover robot ID and check if supported by this service (roomba for now)

                    await this.AuthenticateRoombaAsync(ipAddress, physicalAddress, response.RobotId);
                }

                client.Close();
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed robot discovery.");
                this.logger.LogWarning("Failed iRobot discovery.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    internal class iRobotMcsResponse
    {
        [JsonPropertyName("robotname")]
        public string? RobotName { get; set; }

        [JsonPropertyName("robotid")]
        public string? RobotId { get; set; }
    }

    internal class iRobotWorkerServiceConfiguration
    {
        public List<RoombaConfiguration> RoombaRobots { get; } = new();

        public class RoombaConfiguration
        {
            public RoombaConfiguration(string ipAddress, string physicalAddress, string robotId, string robotPassword)
            {
                this.IpAddress = ipAddress;
                this.PhysicalAddress = physicalAddress;
                this.RobotId = robotId;
                this.RobotPassword = robotPassword;
            }

            public string IpAddress { get; }

            public string PhysicalAddress { get; }

            public string RobotId { get; }

            public string RobotPassword { get; }
        }
    }

    internal static class iRobotChannels
    {
        public const string RoombaChannel = "irobot";
    }

    internal class RoombaMqttStatusDto
    {
        [JsonPropertyName("state")]
        public StateDto? State { get; set; }

        public class StateDto
        {
            [JsonPropertyName("reported")]
            public ReportedDto? Reported { get; set; }

            public class ReportedDto
            {
                [JsonPropertyName("name")] public string? Name { get; set; }

                [JsonPropertyName("batPct")] public double? BatteryPercentage { get; set; }

                [JsonPropertyName("cleanMissionStatus")]
                public CleanMissionStatusDto? CleanMissionStatus { get; set; }

                [JsonPropertyName("pose")] public PoseDto? Pose { get; set; }

                [JsonPropertyName("lastCommand")] public LastCommandDto? LastCommand { get; set; }

                public class LastCommandDto
                {
                    [JsonPropertyName("command")] public string? Command { get; set; }

                    [JsonPropertyName("ordered")] public int? Ordered { get; set; }

                    [JsonPropertyName("pmap_id")] public string? MapId { get; set; }

                    [JsonPropertyName("regions")] public List<RegionDto>? Regions { get; set; }

                    [JsonPropertyName("user_pmapv_id")] public string? UserMapId { get; set; }

                    public class RegionDto
                    {
                        [JsonPropertyName("region_id")] public string? RegionId { get; set; }

                        [JsonPropertyName("type")] public string? Type { get; set; }
                    }
                }

                public class CleanMissionStatusDto
                {
                    [JsonPropertyName("cycle")] public string? Cycle { get; set; }

                    [JsonPropertyName("phase")] public string? Phase { get; set; }

                    [JsonPropertyName("error")] public int? Error { get; set; }

                    [JsonPropertyName("notReady")] public int? NotReady { get; set; }
                }

                public class PoseDto
                {
                    [JsonPropertyName("theta")] public double? Theta { get; set; }

                    [JsonPropertyName("point")] public PointDto? Point { get; set; }

                    public class PointDto
                    {
                        [JsonPropertyName("x")] public double? X { get; set; }

                        [JsonPropertyName("y")] public double? Y { get; set; }
                    }
                }
            }
        }
    }

    internal class RoombaSmartMap
    {
        public RoombaSmartMap(string id)
        {
            this.Id = id;
        }

        public string Id { get; }

        public List<Area>? Rooms { get; set; } = new();

        public List<Area>? Zones { get; set; } = new();

        public class Area
        {
            public Area(string id)
            {
                this.Id = id;
            }

            public string Id { get; }

            public string? Name { get; set; }
        }
    }

    internal class RoombaControl
    {
        private readonly iRobotWorkerServiceConfiguration.RoombaConfiguration config;
        private readonly IMqttClient client;

        public RoombaControl(
            IMqttClientFactory mqttClientFactory,
            iRobotWorkerServiceConfiguration.RoombaConfiguration config)
        {
            if (mqttClientFactory == null) throw new ArgumentNullException(nameof(mqttClientFactory));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.client = mqttClientFactory.Create();
        }
    }
}
