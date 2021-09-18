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
        
        // Caching
        private readonly object cacheLock = new();
        private DateTime? cacheTimeStamp;
        private static readonly TimeSpan CacheValidPeriod = TimeSpan.FromMinutes(1);

        private readonly JsonSerializerSettings deserializationSettings;
        private List<Process>? processes;
        private List<Process>? stateTriggerProcesses;
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

        public async Task<IEnumerable<Process>> GetStateTriggersAsync(CancellationToken cancellationToken)
        {
            await this.CacheProcessesAsync(cancellationToken);
            this.CacheStateTriggers();

            return this.stateTriggerProcesses ?? Enumerable.Empty<Process>();
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
                var stateTriggerConfiguration = string.IsNullOrWhiteSpace(p.ConfigurationSerialized)
                    ? new StateTriggerProcessConfiguration()
                    : JsonConvert.DeserializeObject<StateTriggerProcessConfiguration>(
                        p.ConfigurationSerialized,
                        this.deserializationSettings);
                if (stateTriggerConfiguration == null)
                    throw new Exception($"Failed to process state trigger configuration of process \"{JsonSerializer.Serialize(p)}\".");

                return p with {Configuration = stateTriggerConfiguration};
            }).ToList();
        }

        private async Task CacheProcessesAsync(CancellationToken cancellationToken)
        {
            // Don't cache again if we have cache, and cache valid period didn't expire
            if (this.processes != null &&
                this.cacheTimeStamp.HasValue &&
                DateTime.UtcNow - this.cacheTimeStamp.Value <= CacheValidPeriod)
                return;
            
            try
            {
                this.logger.LogDebug("Loading processes...");

                this.getProcessesTask ??= this.processesClient.GetProcessesAsync(cancellationToken);

                var remoteProcesses = (await this.getProcessesTask).ToList();

                lock (this.cacheLock)
                {
                    if (this.processes != null &&
                        this.cacheTimeStamp.HasValue &&
                        DateTime.UtcNow - this.cacheTimeStamp.Value <= CacheValidPeriod)
                        return;

                    try
                    {
                        this.processes = new List<Process>();
                        foreach (var process in remoteProcesses)
                            this.processes.Add(process);

                        // Invalidate dependency caches
                        this.stateTriggerProcesses = null;
                        this.cacheTimeStamp = DateTime.UtcNow;

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
                this.logger.LogDebug(ex, "Failed to cache processes.");
                throw;
            }
        }
    }
}