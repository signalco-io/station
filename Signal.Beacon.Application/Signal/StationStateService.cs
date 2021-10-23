using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Configuration;

namespace Signal.Beacon.Application.Signal;

internal class StationStateService : IStationStateService
{
    private readonly IConfigurationService configurationService;

    public StationStateService(
        IConfigurationService configurationService)
    {
        this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public async Task<StationState> GetAsync(CancellationToken cancellationToken)
    {
        var config = await this.configurationService.LoadAsync<BeaconConfiguration>("beacon.json", cancellationToken);
        if (string.IsNullOrWhiteSpace(config.Identifier))
            throw new Exception("Can't generate state report without identifier.");

        return new StationState
        {
            Id = config.Identifier,
            Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown"
        };
    }
}