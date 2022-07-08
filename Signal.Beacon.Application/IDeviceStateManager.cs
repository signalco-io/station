using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Entity;

namespace Signal.Beacon.Application;

public interface IDeviceStateManager
{
    IDisposable Subscribe(Func<IEnumerable<ContactPointer>, CancellationToken, Task> handler);

    Task SetStateAsync(ContactPointer target, object? value, CancellationToken cancellationToken);

    Task<object?> GetStateAsync(ContactPointer target, CancellationToken cancellationToken = default);
}