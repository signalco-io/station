using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Signal.Beacon.Application.Signal;

internal class SignalBeaconClient : ISignalBeaconClient
{
    private const string SignalApiBeaconRegisterUrl = "/beacons/register";
    private const string SignalApiBeaconStateUrl = "/beacons/report-state";

    private readonly ISignalClient client;
    private readonly ILogger<SignalBeaconClient> logger;

    public SignalBeaconClient(
        ISignalClient client,
        ILogger<SignalBeaconClient> logger)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RegisterBeaconAsync(string beaconId, CancellationToken cancellationToken)
    {
        await this.client.PostAsJsonAsync(
            SignalApiBeaconRegisterUrl,
            new SignalBeaconRegisterRequestDto(beaconId),
            cancellationToken);
    }

    public async Task ReportAsync(StationState state, CancellationToken cancellationToken)
    {
        try
        {
            var stateDto = new StationStateDto
            {
                Id = state.Id,
                Version = state.Version,
                RunningWorkerServices = state.RunningWorkerServices
            };

            await this.client.PostAsJsonAsync(
                SignalApiBeaconStateUrl,
                stateDto,
                cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning("Failed to report station state to cloud.");
            this.logger.LogTrace(ex, "Station state report failed.");
        }
    }
}