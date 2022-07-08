using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Entity;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application;

public class EntitiesDao : IEntitiesDao
{
    private readonly ISignalcoEntityClient entitiesClient;
    private readonly Lazy<IDeviceStateManager> deviceStateManager;
    private readonly ILogger<EntitiesDao> logger;
    private Dictionary<string, IEntityDetails>? entities;
    private readonly object cacheLock = new();
    private Task<IEnumerable<IEntityDetails>>? getEntitiesTask;

    public EntitiesDao(
        ISignalcoEntityClient devicesClient,
        Lazy<IDeviceStateManager> deviceStateManager,
        ILogger<EntitiesDao> logger)
    {
        this.entitiesClient = devicesClient ?? throw new ArgumentNullException(nameof(devicesClient));
        this.deviceStateManager = deviceStateManager ?? throw new ArgumentNullException(nameof(deviceStateManager));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEntityDetails?> GetByAliasAsync(string alias, CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        return this.entities?.Values.FirstOrDefault(d => d.Alias == alias);
    }
    
    public async Task<IEntityDetails?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        if (this.entities != null && 
            this.entities.TryGetValue(id, out var device))
            return device;
        return null;
    }

    public async Task<IEnumerable<IEntityDetails>> AllAsync(CancellationToken cancellationToken)
    {
        await this.CacheDevicesAsync(cancellationToken);

        return this.entities?.Values.AsEnumerable() ?? Enumerable.Empty<IEntityDetails>();
    }
        
    public Task<object?> ContactAsync(ContactPointer pointer, CancellationToken cancellationToken) => 
        this.deviceStateManager.Value.GetStateAsync(pointer, cancellationToken);

    public void InvalidateEntity()
    {
        lock (this.cacheLock)
        {
            this.entities = null;
        }

        this.logger.LogDebug("Devices cache invalidated");
    }

    private async Task CacheDevicesAsync(CancellationToken cancellationToken)
    {
        if (this.entities != null)
            return;

        try
        {
            this.getEntitiesTask ??= this.entitiesClient.AllAsync(cancellationToken);

            var remoteDevices = (await this.getEntitiesTask).ToList();

            lock (this.cacheLock)
            {
                if (this.entities != null)
                    return;

                try
                {
                    this.entities = new Dictionary<string, IEntityDetails>();
                    foreach (var deviceConfiguration in remoteDevices)
                    {
                        this.entities.Add(deviceConfiguration.Id, deviceConfiguration);

                        // Set local state
                        if (this.deviceStateManager.Value is DeviceStateManager localDeviceStateManager)
                            foreach (var retrievedState in deviceConfiguration.States)
                                localDeviceStateManager.SetLocalState(
                                    retrievedState.contact with { Identifier = deviceConfiguration.Identifier },
                                    retrievedState.value);
                    }
                }
                finally
                {
                    this.getEntitiesTask = null;
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "Cache entities from SignalcoAPI failed.");
            this.logger.LogWarning( "Failed to load entities from Signalco.");
        }
    }
}