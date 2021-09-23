using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Lifetime
{
    public interface IUpdateService
    {
        Task ShutdownAsync();

        Task RestartStationAsync();

        Task UpdateSystemAsync(CancellationToken cancellationToken);

        Task BeginUpdateAsync(CancellationToken cancellationToken);
    }
}