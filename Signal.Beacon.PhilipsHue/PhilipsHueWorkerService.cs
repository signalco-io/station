﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Q42.HueApi;
using Q42.HueApi.Interfaces;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.PhilipsHue
{
    internal class PhilipsHueWorkerService : IWorkerService
    {
        private readonly ICommandHandler<DeviceStateSetCommand> deviceStateSerHandler;
        private readonly ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler;
        private readonly IDevicesDao devicesDao;
        private readonly ILogger<PhilipsHueWorkerService> logger;
        private readonly IConfigurationService configurationService;

        private const int RegisterBridgeRetryTimes = 12;
        private const string PhilipsHueConfigurationFileName = "PhilipsHue.json";

        private PhilipsHueWorkerServiceConfiguration config = new();
        private readonly List<BridgeConnection> bridges = new();
        private readonly Dictionary<string, Light> lights = new();

        public PhilipsHueWorkerService(
            ICommandHandler<DeviceStateSetCommand> deviceStateSerHandler,
            ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler,
            IDevicesDao devicesDao,
            ILogger<PhilipsHueWorkerService> logger,
            IConfigurationService configurationService)
        {
            this.deviceStateSerHandler = deviceStateSerHandler ?? throw new ArgumentNullException(nameof(deviceStateSerHandler));
            this.deviceDiscoveryHandler = deviceDiscoveryHandler ?? throw new ArgumentNullException(nameof(deviceDiscoveryHandler));
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.config = await this.LoadBridgeConfigsAsync(cancellationToken);
            if (!this.config.Bridges.Any())
                _ = this.DiscoverBridgesAsync(true, cancellationToken);
            else
            {
                // Connect to already configured bridges
                foreach (var bridgeConfig in this.config.Bridges.ToList())
                    _ = this.ConnectBridgeAsync(bridgeConfig, cancellationToken);
            }

            _ = Task.Run(() => this.PeriodicalLightStateRefreshAsync(cancellationToken), cancellationToken);
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
                        this.logger.LogWarning(ex, "Failed to refresh devices state.");
                    }
                }

                await Task.Delay(5000, cancellationToken);
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

        private async Task RefreshLightStateAsync(BridgeConnection bridge, Light light, CancellationToken cancellationToken)
        {
            var updatedLight = await bridge.LocalClient.GetLightAsync(light.Id);
            if (updatedLight != null)
            {
                this.lights[light.UniqueId] = updatedLight;
                await this.deviceStateSerHandler.HandleAsync(
                    new DeviceStateSetCommand(new DeviceTarget(PhilipsHueChannels.DeviceChannel, ToSignalDeviceId(light.UniqueId), "state"), updatedLight.State.On), 
                    cancellationToken);
            }
            else
            {
                this.logger.LogWarning(
                    "Light with ID {LightId} not found on bridge {BridgeName}.",
                    light.Id,
                    bridge.Config.Id);
            }
        }

        private async Task<PhilipsHueWorkerServiceConfiguration> LoadBridgeConfigsAsync(CancellationToken cancellationToken) => 
            await this.configurationService.LoadAsync<PhilipsHueWorkerServiceConfiguration>(PhilipsHueConfigurationFileName, cancellationToken);

        private async Task SaveBridgeConfigsAsync(CancellationToken cancellationToken) => 
            await this.configurationService.SaveAsync(PhilipsHueConfigurationFileName, this.config, cancellationToken);

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
                    existingBridge.LocalClient = client;
                else
                {
                    this.bridges.Add(new BridgeConnection
                    {
                        Config = config,
                        LocalClient = client
                    });
                }

                client.Initialize(config.LocalAppKey);
                if (!await client.CheckConnection())
                    throw new SocketException((int) SocketError.TimedOut);

                await this.SyncDevicesWithBridge(config.Id, cancellationToken);
                await this.RefreshDeviceStatesAsync(config.Id, cancellationToken);
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
                var client = bridge.LocalClient;
                var lights = await client.GetLightsAsync();
                foreach (var light in lights)
                {
                    if (string.IsNullOrWhiteSpace(light.UniqueId))
                    {
                        this.logger.LogWarning("Device doesn't have unique ID.");
                        continue;
                    }

                    var existingDevice = await this.devicesDao.GetAsync(light.UniqueId, cancellationToken);
                    if (existingDevice == null)
                        this.NewLight(light, cancellationToken);
                    else throw new NotImplementedException("Updating existing device not supported yet.");

                    this.lights.Add(light.UniqueId, light);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to sync devices.");
            }
        }

        private void NewLight(Light light, CancellationToken cancellationToken)
        {
            var deviceConfig = new DeviceConfiguration(light.Name, ToSignalDeviceId(light.UniqueId))
            {
                Manufacturer = light.ManufacturerName,
                Model = light.ModelId,
                Endpoints = new[]
                {
                    new DeviceEndpoint("main",
                        new[] {new DeviceContact("state", "bool")},
                        new[] {new DeviceContact("state", "bool")})
                }
            };
            this.deviceDiscoveryHandler.HandleAsync(new DeviceDiscoveredCommand(deviceConfig), cancellationToken);
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
                        this.config.Bridges.First(b => b.Id == existingConnection.Config.Id).IpAddress = bridge.IpAddress;
                        await this.SaveBridgeConfigsAsync(cancellationToken);

                        _ = this.ConnectBridgeAsync(existingConnection.Config, cancellationToken);
                    }
                    else if (acceptNewBridges)
                    {
                        var appKey = await client.RegisterAsync("Signal.Beacon.Hue", "HueBeacon");
                        if (appKey == null)
                            throw new Exception("Hub responded with null key.");

                        var bridgeConfig = new BridgeConfig
                        {
                            Id = bridge.BridgeId,
                            IpAddress = bridge.IpAddress,
                            LocalAppKey = appKey
                        };

                        // Persist bridge configuration
                        this.config.Bridges.Add(bridgeConfig);
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
        
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }
}
