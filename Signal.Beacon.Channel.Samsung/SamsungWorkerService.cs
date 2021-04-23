using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Network;
using Signal.Beacon.Core.Workers;
using Websocket.Client;

namespace Signal.Beacon.Channel.Samsung
{
    public class SamsungWorkerService : IWorkerService
    {
        private const string ConfigurationFileName = "Samsung.json";

        private readonly string[] allowedMacCompanies =
        {
            "Samsung Electronics Co.,Ltd"
        };

        private readonly IHostInfoService hostInfoService;
        private readonly IMacLookupService macLookupService;
        private readonly IConfigurationService configurationService;
        private readonly IConductSubscriberClient conductSubscriberClient;
        private readonly ICommandHandler<DeviceDiscoveredCommand> discoverCommandHandler;
        private readonly ICommandHandler<DeviceStateSetCommand> stateSetCommandHandler;
        private readonly ILogger<SamsungWorkerService> logger;
        private SamsungWorkerServiceConfiguration? configuration;
        private readonly List<TvRemote> tvRemotes = new();
        private CancellationToken startCancellationToken;

        public SamsungWorkerService(
            IHostInfoService hostInfoService,
            IMacLookupService macLookupService,
            IConfigurationService configurationService,
            IConductSubscriberClient conductSubscriberClient,
            ICommandHandler<DeviceDiscoveredCommand> discoverCommandHandler,
            ICommandHandler<DeviceStateSetCommand> stateSetCommandHandler,
            ILogger<SamsungWorkerService> logger)
        {
            this.hostInfoService = hostInfoService ?? throw new ArgumentNullException(nameof(hostInfoService));
            this.macLookupService = macLookupService ?? throw new ArgumentNullException(nameof(macLookupService));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.conductSubscriberClient = conductSubscriberClient ?? throw new ArgumentNullException(nameof(conductSubscriberClient));
            this.discoverCommandHandler = discoverCommandHandler ?? throw new ArgumentNullException(nameof(discoverCommandHandler));
            this.stateSetCommandHandler = stateSetCommandHandler ?? throw new ArgumentNullException(nameof(stateSetCommandHandler));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            this.configuration =
                await this.configurationService.LoadAsync<SamsungWorkerServiceConfiguration>(
                    ConfigurationFileName,
                    cancellationToken);

            if (!this.configuration.TvRemotes.Any())
                _ = this.DiscoverDevices();
            else this.configuration.TvRemotes.ForEach(this.ConnectTv);

            this.conductSubscriberClient.Subscribe(SamsungChannels.SamsungChannel, this.SamsungConductHandler);
        }

        private Task SamsungConductHandler(Conduct conduct, CancellationToken cancellationToken)
        {
            var remoteId = conduct.Target.Identifier.Replace("samsung-remote/", "");
            var matchedRemote = this.tvRemotes.FirstOrDefault(r => r.Id == remoteId);
            if (matchedRemote == null)
                throw new Exception($"No matching remote found for target {conduct.Target.Identifier}");

            if (conduct.Target.Contact == "keypress")
            {
                matchedRemote.KeyPress(conduct.Value.ToString() ??
                                       throw new ArgumentException($"Invalid conduct value ${conduct.Value}"));
            } else if (conduct.Target.Contact == "state")
            {
                var boolString = conduct.Value.ToString()?.ToLowerInvariant();
                if (boolString != "true" && boolString != "false")
                    throw new Exception("Invalid contact value type. Expected boolean.");

                // To turn on use WOL, to turn off use power key
                if (boolString == "true")
                    matchedRemote.WakeOnLan();
                else matchedRemote.KeyPress("KEY_POWER");
            }
            else throw new ArgumentOutOfRangeException($"Unsupported contact {conduct.Target.Contact}");

            return Task.CompletedTask;
        }

        private async Task DiscoverDevices()
        {
            var ipAddressesInRange = IpHelper.GetIPAddressesInRange(IpHelper.GetLocalIp());
            var matchedHosts =
                await this.hostInfoService.HostsAsync(ipAddressesInRange, new[] {8001}, this.startCancellationToken);
            var hostsWithPort = matchedHosts.Where(mh => mh.OpenPorts.Count() == 1);
            foreach (var hostInfo in hostsWithPort)
            {
                if (string.IsNullOrWhiteSpace(hostInfo.PhysicalAddress))
                {
                    this.logger.LogDebug("Device MAC not found. Ip: {IpAddress}", hostInfo.IpAddress);
                    continue;
                }

                var deviceCompany =
                    await this.macLookupService.CompanyNameLookupAsync(hostInfo.PhysicalAddress, this.startCancellationToken);
                if (!this.allowedMacCompanies.Contains(deviceCompany))
                {
                    this.logger.LogDebug(
                        "Device MAC not whitelisted. Ip: {IpAddress} Mac: {PhysicalAddress} Company: {MacCompany}",
                        hostInfo.PhysicalAddress, hostInfo.IpAddress, deviceCompany ?? "Not found");
                    continue;
                }

                // TODO: Add to possible matches
                this.logger.LogDebug("Potential Samsung TV Remote device found on address \"{DeviceIp}\"",
                    hostInfo.IpAddress);

                // Try to connect
                var newTvRemoteConfig = new SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig(hostInfo.IpAddress)
                {
                    MacAddress = hostInfo.PhysicalAddress
                };
                this.configuration?.TvRemotes.Add(newTvRemoteConfig);
                this.ConnectTv(newTvRemoteConfig);
            }
        }

        private void ConnectTv(SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig remoteConfig)
        {
            var remote = new TvRemote(remoteConfig, this.logger);
            remote.OnDiscover += (_, command) =>
                this.discoverCommandHandler.HandleAsync(command, this.startCancellationToken);
            remote.OnState += (_, command) =>
                this.stateSetCommandHandler.HandleAsync(command, this.startCancellationToken);
            remote.BeginConnectTv();
            this.tvRemotes.Add(remote);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.configurationService.SaveAsync(ConfigurationFileName, this.configuration, cancellationToken);
        }

        private class TvRemote : IDisposable
        {
            public string? Id => this.configuration.Id;

            private readonly SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration;
            private readonly ILogger logger;
            private IWebsocketClient? client;
            private bool isReconnecting;
            private TvBasicInfoApiV2ResponseDto? tvBasicInfo;
            private bool isDiscovered;

            public event EventHandler<DeviceDiscoveredCommand> OnDiscover;
            public event EventHandler<DeviceStateSetCommand> OnState;


            public TvRemote(SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration, ILogger logger)
            {
                this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public void GetInstalledApps()
            {
                this.client?.Send("{\"method\":\"ms.channel.emit\",\"params\":{\"event\":\"ed.installedApp.get\",\"to\":\"host\"}}");
            }

            public void KeyPress(string keyCode)
            {
                if (this.client == null)
                    throw new Exception("Client not connected.");

                var command =
                    $"{{\"method\":\"ms.remote.control\",\"params\":{{\"Cmd\":\"Click\",\"DataOfCmd\":\"{keyCode}\",\"Option\":\"false\",\"TypeOfRemote\":\"SendRemoteKey\"}}}}";
                this.client.Send(command);
            }

            public void WakeOnLan()
            {
                if (string.IsNullOrWhiteSpace(this.configuration.MacAddress) ||
                    string.IsNullOrWhiteSpace(this.configuration.IpAddress))
                    throw new Exception("MAC and IP address are required for WOL");

                IpHelper.SendWakeOnLan(
                    PhysicalAddress.Parse(this.configuration.MacAddress),
                    IPAddress.Parse(this.configuration.IpAddress));
            }

            public async void BeginConnectTv()
            {
                try
                {
                    this.Disconnect();

                    this.isReconnecting = false;

                    await this.GetBasicInfoAsync();
                    await this.ConnectWsRemoteAsync();
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestTimeout ||
                                                      ex.InnerException is SocketException {SocketErrorCode: SocketError.TimedOut})
                {
                    this.logger.LogDebug("TV Offline {TvIp}", this.configuration.IpAddress);
                    this.ReconnectAfter();
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to connect to TV {TvIp}", this.configuration.IpAddress);
                    this.ReconnectAfter();
                }
            }

            private void ReportOnline(bool isOnline)
            {
                if (this.Id is null) return;

                this.OnState?.Invoke(this, new DeviceStateSetCommand(
                    new DeviceTarget(SamsungChannels.SamsungChannel, this.Id, "state"),
                    isOnline));
            }

            private async Task ConnectWsRemoteAsync()
            {
                this.client = await this.ConnectWsEndpointAsync(this.configuration.IpAddress, 8002,
                    "/channels/samsung.remote.control", "Signal", this.configuration.Token);
                this.client.MessageReceived.Subscribe(this.HandleTvRemoteMessage);
            }

            private async Task GetBasicInfoAsync()
            {
                this.tvBasicInfo = await new HttpClient().GetFromJsonAsync<TvBasicInfoApiV2ResponseDto>(
                    $"http://{this.configuration.IpAddress}:8001/api/v2/");
                if (this.tvBasicInfo?.Device != null) 
                    this.configuration.Id = this.tvBasicInfo.Device.Id;
            }

            private void ReconnectAfter(double delayMs = 30000)
            {
                if (this.isReconnecting) return;

                this.ReportOnline(false);

                this.Disconnect();
                Task.Delay(TimeSpan.FromMilliseconds(delayMs))
                    .ContinueWith(_ => this.BeginConnectTv());
            }

            private void Disconnect()
            {
                try
                {
                    this.isReconnecting = true;
                    this.client?.Dispose();
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to dispose the TV connection {TvIp}", this.configuration.IpAddress);
                }
            }

            private void HandleTvRemoteMessage(ResponseMessage message)
            {
                dynamic? response = JsonConvert.DeserializeObject(message.Text);

                this.ReportOnline(true);

                // Acquire token procedure
                if (response?.data?.token != null)
                {
                    var token = response.data.token.ToString();
                    if (string.IsNullOrWhiteSpace(token)) return;

                    // Persist acquired token
                    this.configuration.Token = token;
                    // TODO: Save config

                    // Reconnect using token
                    this.client?.Dispose();
                    this.BeginConnectTv();
                }

                // Dispatch discovered when token is acquired
                if (!this.isDiscovered && this.configuration.Token != null && this.Id != null && this.tvBasicInfo?.Device != null)
                {
                    this.OnDiscover?.Invoke(this, new DeviceDiscoveredCommand(
                        this.tvBasicInfo.Device.Name ?? this.Id, 
                        this.Id)
                    {
                        Manufacturer = "Samsung",
                        Model = this.tvBasicInfo.Device.ModelName,
                        Endpoints = new []
                        {
                            new DeviceEndpoint(SamsungChannels.SamsungChannel, new []
                            {
                                new DeviceContact("state", "bool", DeviceContactAccess.Read | DeviceContactAccess.Write),
                                new DeviceContact("remote_key", "enum", DeviceContactAccess.Write)
                            })
                        }
                    });

                    this.isDiscovered = true;
                }
            }

            private async Task<IWebsocketClient> ConnectWsEndpointAsync(string ipAddress, int port, string url, string name, string? token)
            {
                const string wsUrl = "wss://{0}:{1}/api/v2{2}?name={3}";
                string urlFormat = wsUrl;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    urlFormat += "&token=" + token;
                }

                var wsClient = new WebsocketClient(
                    new Uri(string.Format(urlFormat, ipAddress, port, url, Convert.ToBase64String(Encoding.UTF8.GetBytes(name)))),
                    () => new ClientWebSocket { Options = { RemoteCertificateValidationCallback = (_, _, _, _) => true } })
                {
                    ReconnectTimeout = TimeSpan.FromSeconds(30),
                    IsReconnectionEnabled = false
                };

                wsClient.ReconnectionHappened.Subscribe(info =>
                    this.logger.LogDebug("Reconnection happened {TvIp}, type: {Type}", this.configuration.IpAddress, info.Type));
                wsClient.DisconnectionHappened.Subscribe(info =>
                {
                    this.logger.LogWarning("DisconnectionHappened {TvIp}, type: {Type}", this.configuration.IpAddress, info.Type);
                    this.ReconnectAfter();
                });
                wsClient.MessageReceived.Subscribe(msg =>
                    this.logger.LogTrace("Message received from {TvIp}: {Message}", this.configuration.IpAddress, msg.Text));

                await wsClient.Start();

                return wsClient;
            }

            [Serializable]
            private class TvBasicInfoApiV2ResponseDto
            {
                public DeviceDto? Device { get; set; }

                public record DeviceDto(string? Id, string? Name, string? ModelName);
            }

            public void Dispose()
            {
                this.Disconnect();
            }
        }
    }
}