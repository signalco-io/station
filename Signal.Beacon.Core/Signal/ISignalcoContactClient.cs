using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Entity;

namespace Signal.Beacon.Core.Signal;

public interface ISignalcoContactClient : ISignalFeatureClient
{
    Task UpsertAsync(ContactUpsertCommand command, CancellationToken cancellationToken);
}