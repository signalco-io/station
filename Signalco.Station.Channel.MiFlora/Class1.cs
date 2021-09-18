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
                this.logger.LogDebug(ex, "Discovery failed.");
            }
        }

        private async Task adapter_DeviceFoundAsync(Adapter sender, DeviceFoundEventArgs args)
        {
            this.logger.LogDebug("BLE Device found: {DevicePath}", args.Device.ObjectPath);
            
            // Attach to device callbacks
            args.Device.ServicesResolved += this.DeviceOnServicesResolved;
            args.Device.Connected += this.DeviceOnConnected;
            args.Device.Disconnected += this.DeviceOnDisconnected;

            try
            {
                this.logger.LogDebug("BLE Device: {DevicePath} connecting...", args.Device.ObjectPath);
                await args.Device.ConnectAsync();
                this.logger.LogDebug("BLE Device: {DevicePath} connected", args.Device.ObjectPath);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to get properties for device {DevicePath}", args.Device.ObjectPath);
            }

            try
            {
                this.logger.LogDebug("BLE Device: {DevicePath} reading services...", args.Device.ObjectPath);
                var services = await args.Device.GetServicesAsync();
                this.logger.LogDebug("BLE Device: {DevicePath} services: {@Services}", args.Device.ObjectPath, services);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to get properties for device {DevicePath}", args.Device.ObjectPath);
            }


            try
            {
                var properties = await args.Device.GetAllAsync();
                this.logger.LogDebug("BLE Device: {DevicePath} properties: {@Properties}", args.Device.ObjectPath, properties);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to get properties for device {DevicePath}", args.Device.ObjectPath);
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
            this.logger.LogDebug("BLE service resolver {State}", args.IsStateChange);
            var services = await sender.GetServicesAsync();
            this.logger.LogDebug("BLE Services: {@Services}", services);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}