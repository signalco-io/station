using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog.Core;
using Serilog.Events;
using Signal.Beacon.Application.Signal;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon;

public class SignalcoStationLoggingSink : ILogEventSink
{
    private readonly Lazy<IStationStateService> stationStateService;
    private readonly Lazy<ISignalBeaconClient> client;
    private DateTime? lastSent;
    private readonly ConcurrentBag<LogEvent> outbox = new();
    private readonly TimeSpan batchPeriod = TimeSpan.FromSeconds(10);

    public SignalcoStationLoggingSink(Lazy<IStationStateService> stationStateService, Lazy<ISignalBeaconClient> client)
    {
        this.stationStateService = stationStateService ?? throw new ArgumentNullException(nameof(stationStateService));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async void Emit(LogEvent logEvent)
    {
        // Push to outbox
        outbox.Add(logEvent);

        // Check if we need to send outbox batch
        if (lastSent != null && !(DateTime.UtcNow - lastSent > batchPeriod)) 
            return;

        // Take all from outbox
        var toSend = new List<LogEvent>();
        lock (outbox)
        {
            lastSent = DateTime.UtcNow;
            while(outbox.TryTake(out var item))
                toSend.Add(item);
        }

        try
        {
            var stationState = await this.stationStateService.Value.GetAsync(CancellationToken.None);
            await this.client.Value.LogAsync(
                stationState.Id,
                toSend.Select(i => new Entry(i.Timestamp, (int) i.Level, i.RenderMessage())),
                CancellationToken.None);
        }
        catch
        {
            // Failed to log
        }
    }

    private record Entry(DateTimeOffset TimeStamp, int Level, string Message) : ISignalcoStationLoggingEntry;
}