using System;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Devices;

namespace Signal.Beacon.Application
{
    public interface IDeviceStateManager
    {
        IDisposable Subscribe(Func<DeviceTarget, CancellationToken, Task> handler);

        Task SetStateAsync(DeviceTarget target, object? value, CancellationToken cancellationToken);

        Task<object?> GetStateAsync(DeviceTarget target);
    }
}