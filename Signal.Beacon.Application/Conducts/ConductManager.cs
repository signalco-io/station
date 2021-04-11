using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application.PubSub;
using Signal.Beacon.Application.Signal.SignalR;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Structures.Queues;

namespace Signal.Beacon.Application.Conducts
{
    internal class ConductManager : IConductManager
    {
        private readonly IPubSubTopicHub<Conduct> conductHub;
        private readonly ISignalSignalRConductsHubClient signalRConductsHubClient;
        private readonly IDevicesDao devicesDao;
        private readonly IDelayedQueue<ConductRequestDto> delayedConducts = new DelayedQueue<ConductRequestDto>();
        private readonly ILogger<ConductManager> logger;


        public ConductManager(
            IPubSubTopicHub<Conduct> conductHub,
            ISignalSignalRConductsHubClient signalRConductsHubClient,
            IDevicesDao devicesDao,
            ILogger<ConductManager> logger)
        {
            this.conductHub = conductHub ?? throw new ArgumentNullException(nameof(conductHub));
            this.signalRConductsHubClient = signalRConductsHubClient ?? throw new ArgumentNullException(nameof(signalRConductsHubClient));
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task ConductRequestedHandlerAsync(ConductRequestDto request, bool ignoreDelay, CancellationToken cancellationToken)
        {
            var device = await this.devicesDao.GetByIdAsync(request.DeviceId, cancellationToken);
            if (device != null)
            {
                // Publish right away if no delay or ignored
                if (ignoreDelay || request.Delay <= 0)
                {
                    await this.PublishAsync(new[]
                    {
                        new Conduct(
                            new DeviceTarget(request.ChannelName, device.Identifier, request.ContactName),
                            request.ValueSerialized,
                            request.Delay ?? 0)
                    }, cancellationToken);
                }
                else
                {
                    this.delayedConducts.Enqueue(request, TimeSpan.FromMilliseconds(request.Delay ?? 0));
                }
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await this.signalRConductsHubClient.OnConductRequestAsync(
                (req, conductCancellationToken) =>
                    this.ConductRequestedHandlerAsync(req, false, conductCancellationToken),
                cancellationToken);

            _ = Task.Run(() => this.DelayedConductsLoop(cancellationToken), cancellationToken);
        }

        private async Task DelayedConductsLoop(CancellationToken cancellationToken)
        {
            await foreach (var conduct in this.delayedConducts.WithCancellation(cancellationToken))
                await this.ConductRequestedHandlerAsync(conduct, true, cancellationToken);
        }

        public IDisposable Subscribe(string channel, Func<Conduct, CancellationToken, Task> handler) =>
            this.conductHub.Subscribe(new[] {channel}, handler);

        public async Task PublishAsync(IEnumerable<Conduct> conducts, CancellationToken cancellationToken)
        {
            var enumerable = conducts?.ToList() ?? new List<Conduct>();
            foreach (var conduct in enumerable) 
                this.logger.LogDebug("Publishing conduct {Target} {Value} (after {Delay}ms)", conduct.Target, conduct.Value, conduct.Delay);

            await Task.WhenAll(
                enumerable
                    .GroupBy(c => c.Target.Channel)
                    .Select(cGroup => this.conductHub.PublishAsync(cGroup.Key, cGroup, cancellationToken)));

            // TODO: Publish to SignalR if no local handler successfully handled the conduct
        }
    }
}
