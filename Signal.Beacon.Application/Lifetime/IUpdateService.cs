using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Lifetime
{
    public interface IUpdateService
    {
        Task BeginUpdateAsync(CancellationToken cancellationToken);
    }
}