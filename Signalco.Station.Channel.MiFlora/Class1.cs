using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HashtagChris.DotNetBlueZ;
using HashtagChris.DotNetBlueZ.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Workers;

namespace Signalco.Station.Channel.MiFlora
{
    public static class MiFloraWorkerServiceCollectionExtensions
    {
        public static IServiceCollection AddMiFlora(this IServiceCollection services)
        {
            return services
                .AddTransient<IWorkerService, MiFloraWorkerService>();
        }
    }

    internal class MiFloraChannels
    {
        public const string MiFlora = "miflora";
    }

    internal class MiFloraWorkerService : IWorkerService
    {
        private readonly IDevicesDao devicesDao;
        private readonly ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler;
        private readonly ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler;
        private readonly ICommandHandler<DeviceStateSetCommand> deviceStateHandler;
        private readonly ILogger<MiFloraWorkerService> logger;
        private static readonly SemaphoreSlim btLock = new(1, 1);
        private CancellationToken startCancellationToken;
        private Adapter? adapter;
        private readonly List<string> knownDevices = new();
        private readonly List<string> ignoredDevices = new();

        public MiFloraWorkerService(
            IDevicesDao devicesDao,
            ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler,
            ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler,
            ICommandHandler<DeviceStateSetCommand> deviceStateHandler,
            ILogger<MiFloraWorkerService> logger)
        {
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.deviceDiscoveryHandler = deviceDiscoveryHandler ?? throw new ArgumentNullException(nameof(deviceDiscoveryHandler));
            this.deviceContactUpdateHandler = deviceContactUpdateHandler ?? throw new ArgumentNullException(nameof(deviceContactUpdateHandler));
            this.deviceStateHandler = deviceStateHandler ?? throw new ArgumentNullException(nameof(deviceStateHandler));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            _ = Task.Run(() => this.PoolDevicesLoop(cancellationToken), cancellationToken);
        }

        private async Task BeginDiscoveryAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("Started discovery...");
            
            try
            {
                this.adapter = (await BlueZManager.GetAdaptersAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).FirstOrDefault();
                if (this.adapter == null)
                    throw new Exception("No BT adapter available.");
                this.logger.LogDebug("Using adapter: {AdapterName}", this.adapter.ObjectPath);

                // Start device discovery
                await this.adapter.StartDiscoveryAsync().WaitAsync(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Discovery start failed");
            }
        }

        private async Task PoolDevicesLoop(CancellationToken cancellationToken)
        {
            await this.BeginDiscoveryAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await this.ProcessDevicesAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
            }
        }

        private async Task ProcessDevicesAsync(CancellationToken cancellationToken)
        {
            if (this.adapter == null)
                return;

            // Process known devices
            var devices = await this.adapter.GetDevicesAsync();
            foreach (var device in devices) 
                await this.ProcessDeviceAsync(device, cancellationToken);
        }

        private async Task ProcessDeviceAsync(IDevice1 btDevice, CancellationToken cancellationToken)
        {
            await btLock.WaitAsync(cancellationToken);

            try
            {
                // Skip if in ignored devices list
                if (this.ignoredDevices.Contains(btDevice.ObjectPath.ToString()))
                {
                    this.logger.LogTrace("BLE Device {DevicePath} ignored", btDevice.ObjectPath);
                    return;
                }

                // See if this is device we are interested info and add it to known devices list
                if (!this.knownDevices.Contains(btDevice.ObjectPath.ToString()))
                {
                    // Wait for name property at most N seconds
                    this.logger.LogDebug("BLE Device: {DevicePath} discovery...", btDevice.ObjectPath);
                    var deviceName = await btDevice
                        .GetAsync<string>("Name")
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                    // Ignore if not flower care or did not respond in time
                    if (string.IsNullOrWhiteSpace(deviceName) ||
                        !deviceName.Contains("Flower care"))
                    {
                        this.ignoredDevices.Add(btDevice.ObjectPath.ToString());
                        this.logger.LogDebug(
                            "BLE Device {DevicePath} added to ignored devices because it didn't match",
                            btDevice.ObjectPath);
                        return;
                    }

                    this.knownDevices.Add(btDevice.ObjectPath.ToString());
                    this.logger.LogDebug("BLE Device {DevicePath} added to known devices", btDevice.ObjectPath);

                    // Discover device
                    var deviceConfig = new DeviceDiscoveredCommand(
                        deviceName,
                        $"{MiFloraChannels.MiFlora}/{await btDevice.GetAddressAsync()}");

                    await this.deviceDiscoveryHandler.HandleAsync(deviceConfig, cancellationToken);
                }

                // Skip if device is not in known devices list
                if (!this.knownDevices.Contains(btDevice.ObjectPath.ToString()))
                {
                    this.logger.LogTrace("BLE Device {DevicePath} ignored", btDevice.ObjectPath);
                    return;
                }

                // Retrieve device from DAO
                var deviceIdentifier = $"{MiFloraChannels.MiFlora}/{await btDevice.GetAddressAsync()}";
                var device = await this.devicesDao.GetAsync(deviceIdentifier, cancellationToken);
                if (device == null)
                {
                    this.logger.LogWarning("Failed to update device contacts because device with Identifier: {DeviceIdentifier} is not found.", deviceIdentifier);
                    return;
                }

                try
                {
                    // Try to connect
                    await btDevice.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (Exception ex)
                {
                    this.logger.LogDebug(ex, "Failed to connect to device {DevicePath}", btDevice.ObjectPath);
                    return;
                }

                try
                {
                    // Try to retrieve service
                    var floraService = await btDevice
                        .GetServiceAsync("00001204-0000-1000-8000-00805f9b34fb")
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                    // Force data mode
                    var writeModel = await floraService.GetCharacteristicAsync("00001a00-0000-1000-8000-00805f9b34fb");
                    await writeModel
                        .WriteValueAsync(new byte[] { 0xA0, 0x1F }, new Dictionary<string, object>())
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    await Task.Delay(500, cancellationToken);

                    // Get sensor data characteristic
                    var sensorData = await floraService
                        .GetCharacteristicAsync("00001a01-0000-1000-8000-00805f9b34fb")
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    
                    // Read sensor data
                    var sensorDataValue = await sensorData.ReadValueAsync(TimeSpan.FromSeconds(10));
                    this.logger.LogTrace("Flora sensor data: {@Data}", sensorDataValue);

                    // Parse sensor data
                    var temperature = (short)(sensorDataValue[1] << 8 | sensorDataValue[0]) / 10d;
                    var moisture = (int)sensorDataValue[7];
                    var light = sensorDataValue[3] + sensorDataValue[4] * 256;
                    var conductivity = sensorDataValue[8] + sensorDataValue[9] * 256;

                    this.logger.LogDebug("Moisture: {MoistureValue}, Light: {LightValue}, Conductivity: {ConductivityValue}, Temperature: {TemperatureValue}", moisture, light, conductivity, temperature);

                    // Validate values
                    if (temperature is < -30 or > 80 ||
                        moisture is < 0 or > 100 ||
                        light < 0 ||
                        conductivity is < 0 or > 20000)
                    {
                        // TODO: Invalidate read
                        this.logger.LogDebug(
                            "Ignored values of device {DevicePath} because they are out of range",
                            btDevice.ObjectPath);
                        return;
                    }

                    // Discover contacts
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                        device, MiFloraChannels.MiFlora, "temperature",
                        c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "moisture",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "light",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "conductivity",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);

                    // Set values
                    await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                            new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "temperature"), temperature),
                        cancellationToken);
                    await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                            new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "moisture"), moisture),
                        cancellationToken);
                    await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                            new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "light"), light),
                        cancellationToken);
                    await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                            new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "conductivity"), conductivity),
                        cancellationToken);

                    //var versionBattery =
                    //    await floraService.GetCharacteristicAsync("00001a02-0000-1000-8000-00805f9b34fb");
                    //this.logger.LogDebug("Flora service retrieved {Path}", floraService.ObjectPath);
                    //var versionBatteryValue = await versionBattery.ReadValueAsync(TimeSpan.FromSeconds(5));
                    //this.logger.LogDebug("Flora version and battery data: {@Data}", versionBatteryValue);

                    // TODO: Parse version and battery data
                }
                catch (Exception ex)
                {
                    this.logger.LogDebug(ex, "Failed to retrieve device {DevicePath} data", btDevice.ObjectPath);
                }

                try
                {
                    await btDevice.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to disconnect from device {DevicePath}", btDevice.ObjectPath);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(
                    ex, 
                    "Failed to process device {DevicePath}",
                    btDevice.ObjectPath);
            }
            finally
            {
                btLock.Release();
            }
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}