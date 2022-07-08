using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Core.Entity;

public interface IEntitiesDao
{
    Task<IEntityDetails?> GetAsync(string id, CancellationToken cancellationToken);

    Task<IEntityDetails?> GetByAliasAsync(string alias, CancellationToken cancellationToken);

    Task<IEnumerable<IEntityDetails>> AllAsync(CancellationToken cancellationToken);
    
    void InvalidateEntity();
}