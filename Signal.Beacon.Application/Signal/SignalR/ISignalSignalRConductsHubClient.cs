using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Signal.SignalR;

public interface ISignalSignalRConductsHubClient : ISignalSignalRHubClient
{
    Task OnConductRequestAsync(Func<ConductRequestDto, CancellationToken, Task> handler, CancellationToken cancellationToken);

    Task OnConductRequestMultipleAsync(
        Func<IEnumerable<ConductRequestDto>, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}