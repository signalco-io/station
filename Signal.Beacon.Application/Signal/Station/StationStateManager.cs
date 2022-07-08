using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application.Signal.Client.Station;

namespace Signal.Beacon.Application.Signal.Station;

internal class StationStateManager : IStationStateManager
{
    private readonly ISignalcoStationClient signalClient;
    private readonly IStationStateService stationStateService;
    private readonly IWorkerServiceManager workerServiceManager;
    private readonly ILogger<StationStateManager> logger;

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ManagerCancellationToken => cts.Token;


    public StationStateManager(
        ISignalcoStationClient stationClient,
        IStationStateService stationStateService,
        IWorkerServiceManager workerServiceManager,
        ILogger<StationStateManager> logger)
    {
        this.stationStateService = stationStateService ?? throw new ArgumentNullException(nameof(stationStateService));
        this.workerServiceManager = workerServiceManager ?? throw new ArgumentNullException(nameof(workerServiceManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        signalClient = stationClient ?? throw new ArgumentNullException(nameof(stationClient));
    }


    public Task BeginMonitoringStateAsync(CancellationToken cancellationToken)
    {
        workerServiceManager.OnChange += WorkerServiceManagerOnOnChange;

        Task.Run(async () => await PeriodicStatusReportsAsync(), cancellationToken);

        logger.LogDebug("Started monitoring station state...");

        return Task.CompletedTask;
    }

    private async Task PeriodicStatusReportsAsync()
    {
        while (!ManagerCancellationToken.IsCancellationRequested)
        {
            await StateChangedAsync(ManagerCancellationToken);
            await Task.Delay(TimeSpan.FromMinutes(3), ManagerCancellationToken);
        }
    }

    private void WorkerServiceManagerOnOnChange(object? _, IWorkerServiceManagerStateChangeEventArgs e) => _ = StateChangedAsync(ManagerCancellationToken);

    private async Task StateChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            // TODO: Report state to station entity
            //var state = await stationStateService.GetAsync(cancellationToken);
            //await signalClient.ReportAsync(state, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogTrace(ex, "Filed to report Station state to cloud");
            logger.LogDebug("Failed to report Station state to cloud");
        }
    }

    public void Dispose()
    {
        cts.Cancel();
    }
}