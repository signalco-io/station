using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HashtagChris.DotNetBlueZ;
using HashtagChris.DotNetBlueZ.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    public class MiFloraWorkerService : IWorkerService
    {
        private readonly ILogger<MiFloraWorkerService> logger;
        private static readonly SemaphoreSlim btLock = new(1, 1);
        private CancellationToken startCancellationToken;
        private Adapter? adapter;
        private List<string> knownDevices = new();
        private List<string> ignoredDevices = new();

        public MiFloraWorkerService(
            ILogger<MiFloraWorkerService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            _ = Task.Run(() => this.BeginDiscoveryAsync(cancellationToken), cancellationToken);
            _ = Task.Run(() => this.PoolDevicesLoop(cancellationToken), cancellationToken);
        }

        private async Task BeginDiscoveryAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("Started discovery...");
            
            try
            {
                this.adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault();
                if (this.adapter == null)
                    throw new Exception("No BT adapter available.");
                this.logger.LogDebug("Using adapter: {AdapterName}", this.adapter.ObjectPath);

                // Start device discovery
                await this.adapter.StartDiscoveryAsync();
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Discovery start failed");
            }
        }

        private async Task PoolDevicesLoop(CancellationToken cancellationToken)
        {
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

        private async Task ProcessDeviceAsync(IDevice1 device, CancellationToken cancellationToken)
        {
            await btLock.WaitAsync(cancellationToken);

            try
            {
                // Skip if in ignored devices list
                if (this.ignoredDevices.Contains(device.ObjectPath.ToString()))
                {
                    this.logger.LogTrace("BLE Device {DevicePath} ignored", device.ObjectPath);
                    return;
                }

                // See if this is device we are interested info and add it to known devices list
                if (!this.knownDevices.Contains(device.ObjectPath.ToString()))
                {
                    // Wait for name property at most N seconds
                    this.logger.LogDebug("BLE Device: {DevicePath} discovery...", device.ObjectPath);
                    var deviceNameTask = await device
                        .GetAsync<string>("Name")
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                    // Ignore if not flower care or did not respond in time
                    if (string.IsNullOrWhiteSpace(deviceNameTask) ||
                        !deviceNameTask.Contains("Flower care"))
                    {
                        this.ignoredDevices.Add(device.ObjectPath.ToString());
                        this.logger.LogTrace("BLE Device {DevicePath} added to ignored devices because it didn't match", device.ObjectPath);
                        return;
                    }

                    this.knownDevices.Add(device.ObjectPath.ToString());
                    this.logger.LogTrace("BLE Device {DevicePath} added to known devices", device.ObjectPath);
                }

                // Skip if device is not in known devices list
                if (!this.knownDevices.Contains(device.ObjectPath.ToString()))
                {
                    this.logger.LogTrace("BLE Device {DevicePath} ignored", device.ObjectPath);
                    return;
                }

                try
                {
                    // Try to connect
                    await device.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (Exception ex)
                {
                    this.logger.LogDebug(ex, "Failed to connect to device {DevicePath}", device.ObjectPath);
                    return;
                }

                try
                {
                    // Try to retrieve service
                    var floraService = await device
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
                    var temperature = (short)(sensorDataValue[0] << 8 | sensorDataValue[1]);
                    var moisture = (int)sensorDataValue[7];
                    var light = sensorDataValue[3] + sensorDataValue[4] * 256;
                    var conductivity = sensorDataValue[8] + sensorDataValue[9] * 256;

                    this.logger.LogDebug("Moisture: {MoistureValue}, Light: {LightValue}, Conductivity: {ConductivityValue}", moisture, light, conductivity);

                    // Validate values
                    if (temperature is < -30 or > 80 ||
                        moisture is < 0 or > 100 ||
                        light < 0 ||
                        conductivity is < 0 or > 20000)
                    {
                        // TODO: Invalidate read
                        this.logger.LogDebug(
                            "Ignored values of device {DevicePath} because they are out of range",
                            device.ObjectPath);
                        return;
                    }

                    //var versionBattery =
                    //    await floraService.GetCharacteristicAsync("00001a02-0000-1000-8000-00805f9b34fb");
                    //this.logger.LogDebug("Flora service retrieved {Path}", floraService.ObjectPath);
                    //var versionBatteryValue = await versionBattery.ReadValueAsync(TimeSpan.FromSeconds(5));
                    //this.logger.LogDebug("Flora version and battery data: {@Data}", versionBatteryValue);

                    // TODO: Parse version and battery data
                }
                catch (Exception ex)
                {
                    this.logger.LogDebug(ex, "Failed to retrieve device {DevicePath} data", device.ObjectPath);
                }

                try
                {
                    await device.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to disconnect from device {DevicePath}", device.ObjectPath);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(
                    ex, 
                    "Failed to process device {DevicePath}",
                    device.ObjectPath);
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