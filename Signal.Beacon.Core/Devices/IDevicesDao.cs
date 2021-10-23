using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Values;

namespace Signal.Beacon.Core.Devices;

public interface IDevicesDao
{
    Task<DeviceConfiguration?> GetAsync(string identifier, CancellationToken cancellationToken);
        
    Task<IEnumerable<DeviceConfiguration>> GetAllAsync(CancellationToken cancellationToken);
        
    Task<object?> GetStateAsync(DeviceTarget deviceTarget, CancellationToken cancellationToken);
        
    Task<DeviceConfiguration?> GetByAliasAsync(string alias, CancellationToken cancellationToken);

    Task<DeviceContact?> GetInputContactAsync(DeviceTarget target, CancellationToken cancellationToken);

    Task<DeviceConfiguration?> GetByIdAsync(string deviceId, CancellationToken cancellationToken);

    void InvalidateDevice();
}