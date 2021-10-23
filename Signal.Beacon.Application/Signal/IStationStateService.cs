using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Signal;

public interface IStationStateService
{
    Task<StationState> GetAsync(CancellationToken cancellationToken);
}