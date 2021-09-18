using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HashtagChris.DotNetBlueZ;
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

        private async Task adapter_DeviceFoundAsync(Adapter sender, DeviceFoundEventArgs args)
        {
            var properties = await args.Device.GetAllAsync();
            this.logger.LogDebug("BLE Device properties: {@Properties}", properties);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}