using System;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Signal.SignalR;

public interface ISignalSignalRDevicesHubClient : ISignalSignalRHubClient
{
    void OnDeviceState(Func<SignalDeviceStatePublishDto, CancellationToken, Task> handler, CancellationToken cancellationToken);
}