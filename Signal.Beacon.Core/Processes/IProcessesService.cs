using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Core.Processes;

public interface IProcessesService
{
    Task<IEnumerable<Process>> GetStateTriggeredAsync(CancellationToken cancellationToken);

    Task<IEnumerable<Process>> GetAllAsync(CancellationToken cancellationToken);
}