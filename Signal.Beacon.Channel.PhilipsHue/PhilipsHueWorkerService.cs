using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Extensions;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.Channel.PhilipsHue
{
    internal class PhilipsHueWorkerService : IWorkerService
    {
        private const int RegisterBridgeRetryTimes = 12;
        private const string PhilipsHueConfigurationFileName = "PhilipsHue.json";
        private const string LightStateContactName = "on";
        private const string BrightnessContactName = "brightness";
        private const string ColorTemperatureContactName = "color-temperature";
        private const string ColorRgbContactName = "color-rgb";

        private readonly ICommandHandler<DeviceStateSetCommand> deviceStateSetHandler;
        private readonly ICommandValueHandler<DeviceDiscoveredCommand, string> deviceDiscoveryHandler;
        private readonly ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler;
        private readonly IDevicesDao devicesDao;
        private readonly IConductSubscriberClient conductSubscriberClient;
        private readonly ILogger<PhilipsHueWorkerService> logger;
        private readonly IConfigurationService configurationService;

        private PhilipsHueWorkerServiceConfiguration configuration = new();
        private readonly List<BridgeConnection> bridges = new();
        private readonly Dictionary<string, PhilipsHueLight> lights = new();

        public PhilipsHueWorkerService(
            ICommandHandler<DeviceStateSetCommand> deviceStateSerHandler,
            ICommandValueHandler<DeviceDiscoveredCommand, string> deviceDiscoveryHandler,
            ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler,
            IDevicesDao devicesDao,
            IConductSubscriberClient conductSubscriberClient,
            ILogger<PhilipsHueWorkerService> logger,
            IConfigurationService configurationService)
        {
            this.deviceStateSetHandler = deviceStateSerHandler ?? throw new ArgumentNullException(nameof(deviceStateSerHandler));
            this.deviceDiscoveryHandler = deviceDiscoveryHandler ?? throw new ArgumentNullException(nameof(deviceDiscoveryHandler));
            this.deviceContactUpdateHandler = deviceContactUpdateHandler ?? throw new ArgumentNullException(nameof(deviceContactUpdateHandler));
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.conductSubscriberClient = conductSubscriberClient ?? throw new ArgumentNullException(nameof(conductSubscriberClient));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.configuration = await this.LoadBridgeConfigsAsync(cancellationToken);
            if (!this.configuration.Bridges.Any())
                _ = this.DiscoverBridgesAsync(true, cancellationToken);
            else
            {
                // Connect to already configured bridges
                foreach (var bridgeConfig in this.configuration.Bridges.ToList())
                    _ = this.ConnectBridgeAsync(bridgeConfig, cancellationToken);
            }

            _ = Task.Run(() => this.PeriodicalLightStateRefreshAsync(cancellationToken), cancellationToken);

            this.conductSubscriberClient.Subscribe(PhilipsHueChannels.DeviceChannel, this.ConductHandlerAsync);
        }

        private async void BeginStreamClip(BridgeConfig config, CancellationToken cancellationToken)
        {
            var client = new HttpClient(new HttpClientHandler
                { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
            client.DefaultRequestHeaders.Add("hue-application-key", config.LocalAppKey);
            var clipUrl = "https://" + config.IpAddress + "/eventstream/clip/v2";

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var response = await client.GetAsync(clipUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                        await this.RefreshDeviceStatesAsync(config.Id, cancellationToken);
                }
                catch
                {
                    // Wait for next one
                }
            }
        }

        private async Task ConductHandlerAsync(IEnumerable<Conduct> conducts, CancellationToken cancellationToken)
        {
            var conductsTasks = conducts
                .GroupBy(c => c.Target.Identifier)
                .Select(lightIdentifierConducts =>
                    this.ExecuteLightConductsAsync(cancellationToken, lightIdentifierConducts));
            await Task.WhenAll(conductsTasks);
        }

        private async Task ExecuteLightConductsAsync(CancellationToken cancellationToken, IGrouping<string, Conduct>? lightIdentifierConducts)
        {
            if (lightIdentifierConducts == null) return;

            try
            {
                var lightIdentifier = lightIdentifierConducts.Key;
                if (!this.lights.TryGetValue(ToPhilipsHueDeviceId(lightIdentifier), out var light))
                {
                    this.logger.LogWarning(
                        "No light with specified identifier registered. Target identifier: {TargetIdentifier}",
                        lightIdentifier);
                    return;
                }

                // Retrieve bridge connection
                var bridgeConnection = await this.GetBridgeConnectionAsync(light.BridgeId);
                var bridgeLight = await bridgeConnection.LocalClient.GetLightAsync(light.OnBridgeId);
                if (bridgeLight == null)
                {
                    this.logger.LogWarning(
                        "No light with specified identifier found on bridge. Target identifier: {TargetIdentifier}. LightBridgeId: {OnBridgeId}",
                        lightIdentifier, light.OnBridgeId);
                    return;
                }

                // Construct light command from conducts
                var lightCommand = new LightCommand();
                foreach (var conduct in lightIdentifierConducts)
                {
                    try
                    {
                        switch (conduct.Target.Contact)
                        {
                            case LightStateContactName:
                                lightCommand.On = conduct.Value.ToString()?.ToLowerInvariant() == "true";
                                break;
                            case ColorTemperatureContactName:
                                if (!double.TryParse(conduct.Value.ToString(), out var temp))
                                    throw new Exception("Invalid temperature contact value.");

                                lightCommand.ColorTemperature = temp
                                    .NormalizedToMirek(
                                        bridgeLight.Capabilities.Control.ColorTemperature.Min,
                                        bridgeLight.Capabilities.Control.ColorTemperature.Max);
                                break;
                            case BrightnessContactName:
                                if (!double.TryParse(conduct.Value.ToString(), out var brightness))
                                    throw new Exception("Invalid brightness contact value.");

                                lightCommand.Brightness = (byte)brightness.Denormalize(0, 255);
                                break;
                            default:
                                throw new NotSupportedException("Not supported contact.");
                        }
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogTrace(ex, "Couldn't handle conduct {@Conduct}", conduct);
                        this.logger.LogWarning("Conduct error message: {Message} for conduct: {@Conduct}", ex.Message, conduct);
                    }
                }

                // Send the constructed command to the bridge
                this.logger.LogDebug(
                    "Sending command to the bridge {BridgeId}: {@Command}",
                    light.OnBridgeId, lightCommand);
                await bridgeConnection.LocalClient.SendCommandAsync(lightCommand, new[] { light.OnBridgeId });

                // Refresh immediately 
                await this.RefreshLightStateAsync(bridgeConnection, light, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogTrace(ex, "Failed to execute conduct {@Conducts}", lightIdentifierConducts);
                this.logger.LogWarning("Failed to execute conduct {@Conducts}", lightIdentifierConducts);
            }
        }

        private async Task PeriodicalLightStateRefreshAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var bridgeConnection in this.bridges.ToList())
                {
                    try
                    {
                        await this.RefreshDeviceStatesAsync(bridgeConnection.Config.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning("Failed to refresh devices state. Reason: {Reason}", ex.Message);
                    }
                }

                await Task.Delay(10000, cancellationToken);
            }
        }

        private async Task RefreshDeviceStatesAsync(string bridgeId, CancellationToken cancellationToken)
        {
            var bridge = await this.GetBridgeConnectionAsync(bridgeId);
            foreach (var (_, light) in this.lights)
                await this.RefreshLightStateAsync(bridge, light, cancellationToken);
        }

        private async Task<BridgeConnection> GetBridgeConnectionAsync(string id)
        {
            var bridge = this.bridges.FirstOrDefault(b => b.Config.Id == id);
            if (bridge == null)
                throw new Exception("Bridge not unknown or not initialized yet.");
            if (!await bridge.LocalClient.CheckConnection())
                throw new Exception("Bridge not connected.");
            
            return bridge;
        }

        private async Task RefreshLightStateAsync(BridgeConnection bridge, PhilipsHueLight light, CancellationToken cancellationToken)
        {
            var updatedLight = await bridge.LocalClient.GetLightAsync(light.OnBridgeId);
            if (updatedLight != null)
            {
                this.lights.TryGetValue(light.UniqueId, out var oldLight);
                var newLight = updatedLight.AsPhilipsHueLight(bridge.Config.Id);
                this.lights[light.UniqueId] = newLight;

                // Sync state
                if (oldLight == null || !newLight.State.Equals(oldLight.State))
                {
                    await this.deviceStateSetHandler.HandleAsync(
                        new DeviceStateSetCommand(
                            new DeviceTarget(PhilipsHueChannels.DeviceChannel, ToSignalDeviceId(light.UniqueId), LightStateContactName),
                            updatedLight.State.On),
                        cancellationToken);
                    await this.deviceStateSetHandler.HandleAsync(
                        new DeviceStateSetCommand(
                            new DeviceTarget(PhilipsHueChannels.DeviceChannel, ToSignalDeviceId(light.UniqueId), BrightnessContactName),
                            newLight.State.Brightness),
                        cancellationToken);
                    await this.deviceStateSetHandler.HandleAsync(
                        new DeviceStateSetCommand(
                            new DeviceTarget(PhilipsHueChannels.DeviceChannel, ToSignalDeviceId(light.UniqueId), ColorTemperatureContactName),
                            newLight.State.Temperature),
                        cancellationToken);
                }
            }
            else
            {
                this.logger.LogWarning(
                    "Light with ID {LightId} not found on bridge {BridgeName}.",
                    light.UniqueId,
                    bridge.Config.Id);
            }
        }

        private async Task<PhilipsHueWorkerServiceConfiguration> LoadBridgeConfigsAsync(CancellationToken cancellationToken) => 
            await this.configurationService.LoadAsync<PhilipsHueWorkerServiceConfiguration>(PhilipsHueConfigurationFileName, cancellationToken);

        private async Task SaveBridgeConfigsAsync(CancellationToken cancellationToken) => 
            await this.configurationService.SaveAsync(PhilipsHueConfigurationFileName, this.configuration, cancellationToken);

        private async Task ConnectBridgeAsync(BridgeConfig config, CancellationToken cancellationToken)
        {
            try
            {
                this.logger.LogInformation("Connecting to bridges {BridgeId} {BridgeIpAddress}...",
                    config.Id,
                    config.IpAddress);

                ILocalHueClient client = new LocalHueClient(config.IpAddress);

                var existingBridge = this.bridges.FirstOrDefault(b => b.Config.Id == config.Id);
                if (existingBridge != null)
                    existingBridge.AssignNewClient(client);
                else this.bridges.Add(new BridgeConnection(config, client));

                client.Initialize(config.LocalAppKey);
                if (!await client.CheckConnection())
                    throw new SocketException((int) SocketError.TimedOut);

                await this.SyncDevicesWithBridge(config.Id, cancellationToken);
                await this.RefreshDeviceStatesAsync(config.Id, cancellationToken);
                this.BeginStreamClip(config, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException {SocketErrorCode: SocketError.TimedOut} ||
                                       ex is HttpRequestException && ex.InnerException is SocketException
                                       {
                                           SocketErrorCode: SocketError.TimedOut
                                       })
            {
                this.logger.LogWarning(
                    "Bridge {BridgeIp} ({BridgeId}) didn't respond in time. Trying to rediscover on another IP address...",
                    config.IpAddress, config.Id);
                _ = this.DiscoverBridgesAsync(false, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to connect to bridge.");
            }
        }

        private async Task SyncDevicesWithBridge(string bridgeId, CancellationToken cancellationToken)
        {
            try
            {
                var bridge = await this.GetBridgeConnectionAsync(bridgeId);
                var remoteLights = await bridge.LocalClient.GetLightsAsync();
                foreach (var light in remoteLights)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(light.UniqueId))
                        {
                            this.logger.LogWarning("Device doesn't have unique ID.");
                            continue;
                        }

                        await this.LightDiscoveredAsync(bridgeId, light, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogTrace(ex, "Failed to configure device {Name} ({Address})", light.Name, light.UniqueId);
                        this.logger.LogWarning(
                            "Failed to configure device {Name} ({Address})", 
                            light.Name, light.UniqueId);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to sync devices.");
            }
        }
        

        private async Task LightDiscoveredAsync(string bridgeId, Light light, CancellationToken cancellationToken)
        {
            this.lights.Add(light.UniqueId, light.AsPhilipsHueLight(bridgeId));

            // Discover device
            var deviceId = await this.deviceDiscoveryHandler.HandleAsync(
                new DeviceDiscoveredCommand(light.Name, ToSignalDeviceId(light.UniqueId))
                {
                    Manufacturer = light.ManufacturerName,
                    Model = light.ModelId
                }, cancellationToken);

            // Discover contacts
            var device = await this.devicesDao.GetByIdAsync(deviceId, cancellationToken);
            if (device == null)
            {
                this.logger.LogWarning("Can't discover device contacts because device with ID: {DeviceId} is not found.", deviceId);
                return;
            }

            // Update standard contacts
            await this.deviceContactUpdateHandler.HandleManyAsync(
                cancellationToken,
                DeviceContactUpdateCommand.FromDevice(
                    device, PhilipsHueChannels.DeviceChannel, LightStateContactName, c => c with
                    {
                        DataType = "bool",
                        Access = DeviceContactAccess.Read | DeviceContactAccess.Write
                    }),
                new DeviceContactUpdateCommand(deviceId, PhilipsHueChannels.DeviceChannel,
                    device.ContactOrDefault(PhilipsHueChannels.DeviceChannel, BrightnessContactName) with
                    {
                        DataType = "double",
                        Access = DeviceContactAccess.Read | DeviceContactAccess.Write
                    }));

            // Color temperature contact
            var colorTemperatureInfo = light.Capabilities.Control.ColorTemperature;
            if (colorTemperatureInfo.Max > 0 &&
                colorTemperatureInfo.Max != colorTemperatureInfo.Min)
                await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                    device, PhilipsHueChannels.DeviceChannel, ColorTemperatureContactName, c => c with
                    {
                        DataType = "colortemp",
                        Access = DeviceContactAccess.Read | DeviceContactAccess.Write
                    }), cancellationToken);
                
            // Color contact
            var colorInfo = light.Capabilities.Control.ColorGamut;
            if (colorInfo != null)
                await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                    device, PhilipsHueChannels.DeviceChannel, ColorRgbContactName, c => c with
                    {
                        DataType = "colorrgb",
                        Access = DeviceContactAccess.Read | DeviceContactAccess.Write
                    }), cancellationToken);
        }

        private static string ToPhilipsHueDeviceId(string signalId) => signalId[(PhilipsHueChannels.DeviceChannel.Length + 1)..];
        
        private static string ToSignalDeviceId(string uniqueId) => $"{PhilipsHueChannels.DeviceChannel}/{uniqueId}";

        private async Task DiscoverBridgesAsync(bool acceptNewBridges, CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Scanning for bridge...");
            var discoveredBridges = await HueBridgeDiscovery.CompleteDiscoveryAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
            this.logger.LogInformation("Bridges found: {BridgesCount}", discoveredBridges.Count);

            if (discoveredBridges.Count <= 0)
            {
                this.logger.LogInformation("No bridges found.");
                return;
            }

            var retryCounter = 0;
            var bridge = discoveredBridges.First();
            ILocalHueClient client = new LocalHueClient(bridge.IpAddress);
            while (retryCounter < RegisterBridgeRetryTimes &&
                   !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var existingConnection = this.bridges.FirstOrDefault(b => b.Config.Id == bridge.BridgeId);
                    if (existingConnection != null)
                    {
                        existingConnection.Config.IpAddress = bridge.IpAddress;
                        this.logger.LogInformation(
                            "Bridge rediscovered {BridgeIp} ({BridgeId}).",
                            existingConnection.Config.IpAddress, existingConnection.Config.Id);

                        // Persist updated configuration
                        this.configuration.Bridges.First(b => b.Id == existingConnection.Config.Id).IpAddress = bridge.IpAddress;
                        await this.SaveBridgeConfigsAsync(cancellationToken);

                        _ = this.ConnectBridgeAsync(existingConnection.Config, cancellationToken);
                    }
                    else if (acceptNewBridges)
                    {
                        var appKey = await client.RegisterAsync("Signal.Beacon.Hue", "HueBeacon");
                        if (appKey == null)
                            throw new Exception("Hub responded with null key.");

                        var bridgeConfig = new BridgeConfig(bridge.BridgeId, bridge.IpAddress, appKey);

                        // Persist bridge configuration
                        this.configuration.Bridges.Add(bridgeConfig);
                        await this.SaveBridgeConfigsAsync(cancellationToken);

                        _ = this.ConnectBridgeAsync(bridgeConfig, cancellationToken);
                    }

                    break;
                }
                catch (LinkButtonNotPressedException ex)
                {
                    this.logger.LogTrace(ex, "Bridge not connected. Waiting for user button press.");
                    this.logger.LogInformation("Press button on Philips Hue bridge to connect...");
                    // TODO: Broadcast CTA on UI (ask user to press button on bridge)
                    retryCounter++;

                    // Give user some time to press the button
                    await Task.Delay(5000, cancellationToken);
                }
            }
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
