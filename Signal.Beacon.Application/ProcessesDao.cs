using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Process = Signal.Beacon.Core.Processes.Process;

namespace Signal.Beacon.Application;

public class ProcessesDao : IProcessesDao
{
    private readonly ISignalProcessesClient processesClient;
    private readonly ILogger<ProcessesDao> logger;
        
    // Caching
    private readonly object cacheLock = new();
    private DateTime? cacheExpiry;
    private static readonly TimeSpan CacheValidPeriod = TimeSpan.FromMinutes(60);

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

    private void ExtendCacheValidity(TimeSpan? duration = null)
    {
        this.cacheExpiry = DateTime.UtcNow + (duration ?? CacheValidPeriod);
        this.logger.LogDebug("Processes cache valid until {TimeStamp}", this.cacheExpiry.Value);
    }

    private async Task CacheProcessesAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            this.logger.LogDebug("Aborted loading processes because cancellation token is cancelled");
            this.logger.LogDebug("Check who cancelled token. Stack: {StackTrace}", new StackTrace().ToString());
            return;
        }

        // Don't cache again if we have cache, and cache valid period didn't expire
        if (this.processes != null &&
            this.cacheExpiry.HasValue &&
            DateTime.UtcNow - this.cacheExpiry.Value <= TimeSpan.Zero)
            return;

        try
        {
            this.logger.LogDebug("Loading processes...");

            this.getProcessesTask ??= this.processesClient.GetProcessesAsync(cancellationToken);

            var remoteProcesses = (await this.getProcessesTask).ToList();

            lock (this.cacheLock)
            {
                if (this.processes != null &&
                    this.cacheExpiry.HasValue &&
                    DateTime.UtcNow - this.cacheExpiry.Value <= TimeSpan.Zero)
                    return;

                try
                {
                    this.processes = new List<Process>();
                    foreach (var process in remoteProcesses)
                        this.processes.Add(process);

                    // Invalidate dependency caches
                    this.stateTriggerProcesses = null;
                    this.ExtendCacheValidity();

                    this.logger.LogDebug("Loaded {ProcessesCount} processes.", this.processes.Count);
                }
                finally
                {
                    this.getProcessesTask = null;
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // Throw if we don't have local cache
            if (this.processes == null)
                throw;

            this.logger.LogWarning("Can't revalidate processes cache because cloud is unavailable");
            this.ExtendCacheValidity();
        }
        catch (TaskCanceledException)
        {
            // Throw if we don't have local cache
            if (this.processes == null)
                throw;

            this.logger.LogWarning("Can't revalidate processes cache - timeout");
            this.ExtendCacheValidity();
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "Failed to cache processes.");
            throw;
        }
    }
}