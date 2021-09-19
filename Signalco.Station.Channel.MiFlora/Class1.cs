using System;
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
        private static readonly SemaphoreSlim btLock = new SemaphoreSlim(1, 1);
        private CancellationToken startCancellationToken;

        public MiFloraWorkerService(
            ILogger<MiFloraWorkerService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            _ = Task.Run(() => this.BeginDiscoveryAsync(cancellationToken), cancellationToken);
        }

        private async Task BeginDiscoveryAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("Started discovery...");
            
            try
            {
                var adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault();
                if (adapter == null)
                    throw new Exception("No BT adapter available.");
                this.logger.LogDebug("Using adapter: {AdapterName}", adapter.ObjectPath);

                // Process known devices
                var devices = await adapter.GetDevicesAsync();
                foreach (var device in devices)
                {
                    await this.ProcessDevice(device, cancellationToken);
                }
                
                // Start device discovery
                adapter.DeviceFound += this.adapter_DeviceFoundAsync;
                await adapter.StartDiscoveryAsync();

                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Discovery failed");
            }
        }

        private async Task adapter_DeviceFoundAsync(Adapter sender, DeviceFoundEventArgs args)
        {
            await this.ProcessDevice(args.Device, this.startCancellationToken);
        }

        private async Task ProcessDevice(Device device, CancellationToken cancellationToken)
        {
            this.logger.LogDebug("BLE Device: {DevicePath}", device.ObjectPath);


            await btLock.WaitAsync(cancellationToken);

            try
            {
                // Wait for name property at most N seconds
                var deviceNameTask = await device
                    .GetAsync<string>("Name")
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                // Ignore if not flower care or did not respond in time
                if (string.IsNullOrWhiteSpace(deviceNameTask) ||
                    !deviceNameTask.Contains("Flower care"))
                {
                    this.logger.LogDebug("Task result: {@Task}", deviceNameTask);
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

                    var sensorData = await floraService
                        .GetCharacteristicAsync("00001a01-0000-1000-8000-00805f9b34fb")
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    
                    var sensorDataValue = await sensorData.ReadValueAsync(TimeSpan.FromSeconds(10));
                    this.logger.LogDebug("Flora sensor data: {@Data}", sensorDataValue);

                    // TODO: Parse sensor data
                    //int16_t* temp_raw = (int16_t*)val;
                    //float temperature = (*temp_raw) / ((float)10.0);
                    //Serial.printf("-- Temperature: %f\n", temperature);

                    int moisture = sensorDataValue[7];
                    int light = sensorDataValue[3] + sensorDataValue[4] * 256;
                    int conductivity = sensorDataValue[8] + sensorDataValue[9] * 256;
                    
                    this.logger.LogDebug("Moisture: {MoistureValue}, Light: {LightValue}, Conductivity: {ConductivityValue}", moisture, light, conductivity);

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

        private async Task DeviceOnDisconnected(Device sender, BlueZEventArgs eventargs)
        {
            this.logger.LogDebug("BLE Device disconnected: {DevicePath}", sender.ObjectPath);
        }

        private async Task DeviceOnConnected(Device sender, BlueZEventArgs eventargs)
        {
            this.logger.LogDebug("BLE Device connected: {DevicePath}", sender.ObjectPath);
        }

        private async Task DeviceOnServicesResolved(Device sender, BlueZEventArgs args)
        {
            // await btLock.WaitAsync();
            //
            // try
            // {
            //     this.logger.LogDebug("BLE service resolver {State}", args.IsStateChange);
            //     var services = await sender.GetServicesAsync();
            //     this.logger.LogDebug("BLE Services: {@Services}", services);
            //     foreach (var service in services)
            //     {
            //         this.logger.LogDebug("Device: {DevicePath} Service: {Service} UUID: {Uuid}",
            //             sender.ObjectPath,
            //             service.ObjectPath,
            //             await service.GetUUIDAsync());
            //     }
            // }
            // catch (Exception ex)
            // {
            //     this.logger.LogWarning(ex, "Failed to read services for device {DevicePath}", sender.ObjectPath);
            // }
            //
            // btLock.Release();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}