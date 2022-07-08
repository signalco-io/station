using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Entity;

namespace Signal.Beacon.Core.Signal;

public interface ISignalcoEntityClient : ISignalFeatureClient
{
    Task<string> UpsertAsync(EntityUpsertCommand command, CancellationToken cancellationToken);
    
    Task<IEnumerable<IEntityDetails>> AllAsync(CancellationToken cancellationToken);
}