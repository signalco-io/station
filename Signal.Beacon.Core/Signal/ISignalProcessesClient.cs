using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Processes;

namespace Signal.Beacon.Core.Signal;

public interface ISignalProcessesClient : ISignalFeatureClient
{
    Task<IEnumerable<Process>> GetProcessesAsync(CancellationToken cancellationToken);
}