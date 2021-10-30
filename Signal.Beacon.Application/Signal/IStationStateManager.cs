using System;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Signal;

public interface IStationStateManager : IDisposable
{
    Task BeginMonitoringStateAsync(CancellationToken cancellationToken);
}