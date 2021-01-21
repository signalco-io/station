using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Signal.Beacon.Core;
using Signal.Beacon.Core.Conditions;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Processes;
using Signal.Beacon.Core.Signal;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Signal.Beacon.Application
{
    public class ProcessesDao : IProcessesDao
    {
        private readonly ISignalProcessesClient processesClient;
        private readonly ILogger<ProcessesDao> logger;
        private readonly object cacheLock = new();
        private readonly JsonSerializerSettings deserializationSettings;
        private List<Process>? processes;
        private List<StateTriggerProcess>? stateTriggerProcesses;
        private Task<IEnumerable<Process>>? getProcessesTask;

        public ProcessesDao(
            ISignalProcessesClient processesClient,
            ILogger<ProcessesDao> logger)
        {
            this.processesClient = processesClient ?? throw new ArgumentNullException(nameof(processesClient));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            this.deserializationSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Populate,
                Converters =
                {
                    new BestMatchDeserializeConverter<IConditionValue>(
                        typeof(ConditionValueStatic),
                        typeof(ConditionValueDeviceState)),
                    new BestMatchDeserializeConverter<IConditionComparable>(
                        typeof(ConditionValueComparison),
                        typeof(Condition))
                }
            };
        }

        public async Task<IEnumerable<StateTriggerProcess>> GetStateTriggersAsync(CancellationToken cancellationToken)
        {
            await this.CacheProcessesAsync(cancellationToken);
            this.CacheStateTriggers();

            return this.stateTriggerProcesses ?? Enumerable.Empty<StateTriggerProcess>();
        }

        public async Task<IEnumerable<Process>> GetAllAsync(CancellationToken cancellationToken)
        {
            await this.CacheProcessesAsync(cancellationToken);

            return this.processes ?? Enumerable.Empty<Process>();
        }

        private void CacheStateTriggers()
        {
            if (this.processes == null)
                throw new Exception("Cache processes before you can cache state triggered processes.");

            this.stateTriggerProcesses = this.processes.Where(p => p.Type == "statetriggered").Select(p =>
            {
                var process = string.IsNullOrWhiteSpace(p.ConfigurationSerialized)
                    ? new StateTriggerProcess()
                    : JsonConvert.DeserializeObject<StateTriggerProcess>(
                        p.ConfigurationSerialized,
                        this.deserializationSettings);
                if (process == null)
                    throw new Exception($"Failed to process process \"{JsonSerializer.Serialize(p)}\".");

                process.IsDisabled = p.IsDisabled;
                process.Alias = p.Alias;
                process.Id = p.Id;

                return process;
            }).ToList();
        }

        private async Task CacheProcessesAsync(CancellationToken cancellationToken)
        {
            if (this.processes != null)
                return;
            
            try
            {
                this.logger.LogDebug("Loading processes...");

                this.getProcessesTask ??= this.processesClient.GetProcessesAsync(cancellationToken);

                var remoteDevices = (await this.getProcessesTask).ToList();

                lock (this.cacheLock)
                {
                    if (this.processes != null)
                        return;

                    try
                    {
                        this.processes = new List<Process>();
                        foreach (var deviceConfiguration in remoteDevices)
                            this.processes.Add(deviceConfiguration);

                        // Invalidate other caches
                        this.stateTriggerProcesses = null;

                        this.logger.LogDebug("Loaded {ProcessesCount} processes.", this.processes.Count);
                    }
                    finally
                    {
                        this.getProcessesTask = null;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to cache devices.");
                throw;
            }
        }
    }
}