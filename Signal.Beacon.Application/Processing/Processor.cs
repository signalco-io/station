using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application.Conducts;
using Signal.Beacon.Core.Conditions;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Processes;
using Signal.Beacon.Core.Structures.Queues;

namespace Signal.Beacon.Application.Processing
{
    internal class Processor : IProcessor
    {
        private readonly IConditionEvaluatorService conditionEvaluatorService;
        private readonly IProcessesService processesService;
        private readonly IDeviceStateManager deviceStateManager;
        private readonly IConductManager conductManager;
        private readonly ILogger<Processor> logger;

        private readonly IDelayedQueue<Conduct> delayedConducts = new DelayedQueue<Conduct>();
        private readonly IDelayedQueue<StateTriggerProcess> delayedTriggers = new DelayedQueue<StateTriggerProcess>();


        public Processor(
            IConditionEvaluatorService conditionEvaluatorService,
            IProcessesService processesService,
            IDeviceStateManager deviceStateManager,
            IConductManager conductManager,
            ILogger<Processor> logger)
        {
            this.conditionEvaluatorService = conditionEvaluatorService ?? throw new ArgumentNullException(nameof(conditionEvaluatorService));
            this.processesService = processesService ?? throw new ArgumentNullException(nameof(processesService));
            this.deviceStateManager = deviceStateManager ?? throw new ArgumentNullException(nameof(deviceStateManager));
            this.conductManager = conductManager ?? throw new ArgumentNullException(nameof(conductManager));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Subscribe to state changes
            this.deviceStateManager.Subscribe(this.ProcessStateChangedAsync);
            _ = Task.Run(() => this.DelayedConductsLoop(cancellationToken), cancellationToken);
            _ = Task.Run(() => this.DelayedTriggersLoop(cancellationToken), cancellationToken);

            return Task.CompletedTask;
        }

        private async Task DelayedConductsLoop(CancellationToken cancellationToken)
        {
            await foreach (var conduct in this.delayedConducts.WithCancellation(cancellationToken)) 
                await this.conductManager.PublishAsync(new[] {conduct}, cancellationToken);
        }

        private async Task DelayedTriggersLoop(CancellationToken cancellationToken)
        {
            await foreach (var process in this.delayedTriggers.WithCancellation(cancellationToken))
                await this.EvaluateAndExecute(new[] { process }, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
        
        private async Task ProcessStateChangedAsync(DeviceTarget target, CancellationToken cancellationToken)
        {
            var processes = await this.processesService.GetStateTriggeredAsync(cancellationToken);
            var applicableProcesses = processes
                .Where(p => !p.IsDisabled && p.Triggers.Any(t => t == target))
                .ToList();
            if (!applicableProcesses.Any())
            {
                this.logger.LogTrace("Change on target {DeviceEndpointTarget} ignored.", target);
                return;
            }

            // Queue delayed triggers
            foreach (var delayedStateTriggerProcess in applicableProcesses.Where(p => p.Delay > 0))
                this.delayedTriggers.Enqueue(
                    delayedStateTriggerProcess,
                    TimeSpan.FromMilliseconds(delayedStateTriggerProcess.Delay));
            
            // Trigger no-delay processes immediately
            await this.EvaluateAndExecute(applicableProcesses.Where(p => p.Delay <= 0), cancellationToken);
        }

        private async Task EvaluateAndExecute(IEnumerable<StateTriggerProcess> applicableProcesses, CancellationToken cancellationToken)
        {
            var conducts = new List<Conduct>();

            // Collect all process conducts that meet conditions
            foreach (var process in applicableProcesses)
            {
                try
                {
                    // Ignore if condition not met
                    if (!await this.conditionEvaluatorService.IsConditionMetAsync(process.Condition, cancellationToken))
                        continue;

                    // Queue conducts
                    this.logger.LogInformation(
                        "Process \"{ProcessName}\" queued...",
                        process.Alias);
                    conducts.AddRange(process.Conducts);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex,
                        "StateTriggerProcess condition invalid. Recheck your configuration. ProcessName: {ProcessName}",
                        process.Alias);
                }
            }

            // Execute all immediate conducts
            await this.conductManager.PublishAsync(conducts.Where(c => c.Delay <= 0), cancellationToken);

            // Queue delayed conducts
            foreach (var delayedConduct in conducts.Where(c => c.Delay > 0))
                this.delayedConducts.Enqueue(delayedConduct, TimeSpan.FromMilliseconds(delayedConduct.Delay));
        }
    }
}