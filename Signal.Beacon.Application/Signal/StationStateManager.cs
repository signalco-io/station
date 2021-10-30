using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Signal.Beacon.Application.Signal;

internal class StationStateManager : IStationStateManager
{
    private readonly ISignalBeaconClient signalClient;
    private readonly IStationStateService stationStateService;
    private readonly IWorkerServiceManager workerServiceManager;
    private readonly ILogger<StationStateManager> logger;

    private readonly CancellationTokenSource cts = new();
    private CancellationToken ManagerCancellationToken => this.cts.Token;


    public StationStateManager(
        ISignalBeaconClient stationClient,
        IStationStateService stationStateService,
        IWorkerServiceManager workerServiceManager,
        ILogger<StationStateManager> logger)
    {
        this.stationStateService = stationStateService ?? throw new ArgumentNullException(nameof(stationStateService));
        this.workerServiceManager = workerServiceManager ?? throw new ArgumentNullException(nameof(workerServiceManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.signalClient = stationClient ?? throw new ArgumentNullException(nameof(stationClient));
    }


    public Task BeginMonitoringStateAsync(CancellationToken cancellationToken)
    {
        this.workerServiceManager.OnChange += WorkerServiceManagerOnOnChange;

        Task.Run(async () => await this.PeriodicStatusReportsAsync(), cancellationToken);

        this.logger.LogDebug("Started monitoring station state...");

        return Task.CompletedTask;
    }

    private async Task PeriodicStatusReportsAsync()
    {
        while (!this.ManagerCancellationToken.IsCancellationRequested)
        {
            await this.StateChangedAsync(ManagerCancellationToken);
            await Task.Delay(TimeSpan.FromMinutes(3), ManagerCancellationToken);
        }
    }

    private void WorkerServiceManagerOnOnChange(object? _, IWorkerServiceManagerStateChangeEventArgs e) => _ = this.StateChangedAsync(ManagerCancellationToken);

    private async Task StateChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await this.stationStateService.GetAsync(cancellationToken);
            await this.signalClient.ReportAsync(state, cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.LogTrace(ex, "Filed to report Station state to cloud");
            this.logger.LogDebug("Failed to report Station state to cloud");
        }
    }

    public void Dispose()
    {
        this.cts.Cancel();
    }
}