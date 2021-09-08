using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Signal
{
    public interface ISignalBeaconClient
    {
        Task RegisterBeaconAsync(string beaconId, CancellationToken cancellationToken);

        Task ReportAsync(StationState state, CancellationToken cancellationToken);
    }
}