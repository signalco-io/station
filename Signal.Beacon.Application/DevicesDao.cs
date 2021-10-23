using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Signal;
using Signal.Beacon.Core.Values;

namespace Signal.Beacon.Application;

public class DevicesDao : IDevicesDao
{
    private readonly ISignalDevicesClient devicesClient;
    private readonly Lazy<IDeviceStateManager> deviceStateManager;
    private readonly ILogger<DevicesDao> logger;
    private Dictionary<string, DeviceConfiguration>? devices;
    private readonly object cacheLock = new();
    private Task<IEnumerable<DeviceWithState>>? getDevicesTask;

    public DevicesDao(
        ISignalDevicesClient devicesClient,
        Lazy<IDeviceStateManager> deviceStateManager,
        ILogger<DevicesDao> logger)
    {
        this.devicesClient = devicesClient ?? throw new ArgumentNullException(nameof(devicesClient));
        this.deviceStateManager = deviceStateManager ?? throw new ArgumentNullException(nameof(deviceStateManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DeviceConfiguration?> GetByAliasAsync(string alias, CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        return this.devices?.Values.FirstOrDefault(d => d.Alias == alias);
    }

    public async Task<DeviceContact?> GetInputContactAsync(DeviceTarget target, CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        var device = await this.GetAsync(target.Identifier, cancellationToken);
        return device?.Endpoints
            .Where(d => d.Channel == target.Channel)
            .SelectMany(d => d.Contacts)
            .Where(c => c.Access.HasFlag(DeviceContactAccess.Read) || c.Access.HasFlag(DeviceContactAccess.Get))
            .FirstOrDefault(c => c.Name == target.Contact);
    }

    public async Task<DeviceConfiguration?> GetByIdAsync(string deviceId, CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        return this.devices?.Values.FirstOrDefault(d => d.Id == deviceId);
    }

    public async Task<DeviceConfiguration?> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        if (this.devices != null && 
            this.devices.TryGetValue(identifier, out var device))
            return device;
        return null;
    }

    public async Task<IEnumerable<DeviceConfiguration>> GetAllAsync(CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        return this.devices?.Values.AsEnumerable() ?? Enumerable.Empty<DeviceConfiguration>();
    }
        
    public Task<object?> GetStateAsync(DeviceTarget deviceTarget, CancellationToken cancellationToken) => 
        this.deviceStateManager.Value.GetStateAsync(deviceTarget);

    public void InvalidateDevice()
    {
        lock (this.cacheLock)
        {
            this.devices = null;
        }

        this.logger.LogDebug("Devices cache invalidated");
    }

    private async Task CacheDevicesAsync(CancellationToken cancellationToken)
    {
        if (this.devices != null)
            return;

        try
        {
            this.getDevicesTask ??= this.devicesClient.GetDevicesAsync(cancellationToken);

            var remoteDevices = (await this.getDevicesTask).ToList();

            lock (this.cacheLock)
            {
                if (this.devices != null)
                    return;

                try
                {
                    this.devices = new Dictionary<string, DeviceConfiguration>();
                    foreach (var deviceConfiguration in remoteDevices)
                    {
                        this.devices.Add(deviceConfiguration.Identifier, deviceConfiguration);

                        // Set local state
                        if (this.deviceStateManager.Value is DeviceStateManager localDeviceStateManager)
                            foreach (var retrievedState in deviceConfiguration.States)
                                localDeviceStateManager.SetLocalState(
                                    new DeviceTarget(
                                        retrievedState.contact.Channel,
                                        deviceConfiguration.Identifier,
                                        retrievedState.contact.Contact),
                                    retrievedState.value);
                    }
                }
                finally
                {
                    this.getDevicesTask = null;
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "Cache devices from SignalAPI failed.");
            this.logger.LogWarning( "Failed to load devices from Signal.");
        }
    }
}