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

        public MiFloraWorkerService(
            ILogger<MiFloraWorkerService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
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

                // Start discovery
                this.logger.LogDebug("Using adapter: {AdapterName}", adapter.ObjectPath);
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
            this.logger.LogDebug("BLE Device found: {DevicePath}", args.Device.ObjectPath);

            // Attach to device callbacks
            args.Device.ServicesResolved += this.DeviceOnServicesResolved;
            args.Device.Connected += this.DeviceOnConnected;
            args.Device.Disconnected += this.DeviceOnDisconnected;

            // await btLock.WaitAsync();
            //
            // try
            // {
            //     this.logger.LogDebug("BLE Device: {DevicePath} connecting...", args.Device.ObjectPath);
            //     await args.Device.ConnectAsync();
            //     this.logger.LogDebug("BLE Device: {DevicePath} connected", args.Device.ObjectPath);
            // }
            // catch (Exception ex)
            // {
            //     this.logger.LogDebug(ex, "Failed to get properties for device {DevicePath}",
            //         args.Device.ObjectPath);
            // }
            //
            // btLock.Release();
            
            // await btLock.WaitAsync();
            //
            // try
            // {
            //     this.logger.LogDebug("BLE Device: {DevicePath} reading services...", args.Device.ObjectPath);
            //     var services = await args.Device.GetServicesAsync();
            //     this.logger.LogDebug("BLE Device: {DevicePath} services: {@Services}", args.Device.ObjectPath,
            //         services);
            // }
            // catch (Exception ex)
            // {
            //     this.logger.LogDebug(ex, "Failed to get properties for device {DevicePath}",
            //         args.Device.ObjectPath);
            // }
            //
            // btLock.Release();
            
            await btLock.WaitAsync();
            
            try
            {
                var properties = await args.Device.GetAllAsync();
                this.logger.LogDebug("BLE Device: {DevicePath} properties: {@Properties}", args.Device.ObjectPath,
                    properties);
                this.logger.LogDebug("BLE device Alias: {Value}", properties.Alias);
                this.logger.LogDebug("BLE device Address: {Value}", properties.Address);

                var floraService = await args.Device.GetServiceAsync("00001204-0000-1000-8000-00805f9b34fb");
                this.logger.LogDebug("Flora service retrieved {Path}", floraService.ObjectPath);
                
                var sensorData = await floraService.GetCharacteristicAsync("00001a01-0000-1000-8000-00805f9b34fb");
                this.logger.LogDebug("Flora sensor characteristic retrieved {Path}", sensorData.ObjectPath);
                var sensorDataValue = await sensorData.ReadValueAsync(TimeSpan.FromSeconds(5));
                this.logger.LogDebug("Flora sensor data: {Data}", sensorDataValue);
                
                var versionBattery = await floraService.GetCharacteristicAsync("00001a02-0000-1000-8000-00805f9b34fb");
                this.logger.LogDebug("Flora service retrieved {Path}", floraService.ObjectPath);
                var versionBatteryValue = await versionBattery.ReadValueAsync(TimeSpan.FromSeconds(5));
                this.logger.LogDebug("Flora version and battery data: {Data}", versionBatteryValue);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to get properties for device {DevicePath}",
                    args.Device.ObjectPath);
            }

            btLock.Release();
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