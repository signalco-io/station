using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application.Signal;

public interface ISignalBeaconClient
{
    Task RegisterBeaconAsync(string beaconId, CancellationToken cancellationToken);

    Task ReportAsync(StationState state, CancellationToken cancellationToken);

    Task LogAsync(string stationId, IEnumerable<ISignalcoStationLoggingEntry> entries, CancellationToken cancellationToken);
}