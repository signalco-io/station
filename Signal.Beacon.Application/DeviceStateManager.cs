using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application.PubSub;
using Signal.Beacon.Core.Entity;
using Signal.Beacon.Core.Extensions;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application;

public class DeviceStateManager : IDeviceStateManager
{
    private readonly IPubSubHub<ContactPointer> deviceStateHub;
    private readonly ISignalcoEntityClient signalClient;
    private readonly IEntitiesDao devicesDao;
    private readonly ILogger<DeviceStateManager> logger;
    private readonly ConcurrentDictionary<ContactPointer, object?> states = new();


    public DeviceStateManager(
        ISignalcoEntityClient signalClient,
        IEntitiesDao entitiesDao,
        IPubSubHub<ContactPointer> deviceStateHub,
        ILogger<DeviceStateManager> logger)
    {
        this.deviceStateHub = deviceStateHub ?? throw new ArgumentNullException(nameof(deviceStateHub));
        this.signalClient = signalClient ?? throw new ArgumentNullException(nameof(signalClient));
        this.devicesDao = entitiesDao ?? throw new ArgumentNullException(nameof(entitiesDao));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    public IDisposable Subscribe(Func<IEnumerable<ContactPointer>, CancellationToken, Task> handler) => 
        this.deviceStateHub.Subscribe(this, handler);

    internal void SetLocalState(ContactPointer target, object? value) => this.states.AddOrSet(target, value);

    public async Task SetStateAsync(ContactPointer target, object? value, CancellationToken cancellationToken)
    {
        var setValue = ParseValue(value);

        // Retrieve device
        var entity = await this.devicesDao.GetAsync(target.EntityId, cancellationToken);
        if (entity == null)
        {
            this.logger.LogDebug("Entity with identifier not found {EntityId} {Contact}: {Value}. State ignored",
                target.EntityId,
                target.Name,
                setValue);
            return;
        }

        // Retrieve contact
        var contact = entity.Contact(target);
        if (contact == null)
        {
            this.logger.LogTrace(
                "Entity contact not found {EntityId} {Contact}: {Value}. State ignored",
                target.EntityId,
                target.Name,
                setValue);
            return;
        }

        // Ignore if value didn't change, don't ignore for actions
        // TODO: Implement as before for actions
        var currentState = ParseValue(await this.GetStateAsync(target));
        var oldAndNewNull = currentState == null && setValue == null;
        var isActionOrStringOrEnum = false; // contact.DataType is "action" or "string" or "enum";
        var areEqual = currentState?.Equals(setValue) ?? false;
        var areEqualValues = currentState == setValue;
        if (oldAndNewNull || 
            !isActionOrStringOrEnum && (areEqual || areEqualValues))
        {
            this.logger.LogTrace(
                "Entity state ignore because it didn't change. {EntityId} {Contact}: {Value}",
                target.EntityId, 
                target.Name,
                setValue);
            return;
        }

        // Apply noise reducing delta
        // TODO: Re-implement noise reduction
        //if (contact.DataType == "double" && 
        //    contact.NoiseReductionDelta.HasValue)
        //{
        //    var currentValueDouble = ParseValueDouble(currentState);
        //    var setValueDouble = ParseValueDouble(setValue);
        //    if (currentValueDouble != null &&
        //        setValueDouble != null &&
        //        Math.Abs(currentValueDouble.Value - setValueDouble.Value) <= contact.NoiseReductionDelta.Value)
        //    {
        //        this.logger.LogTrace(
        //            "Device contact noise reduction threshold not reached. State ignored. {EntityId} {Contact}: {Value}",
        //            target.Identifier,
        //            target.Contact,
        //            setValue);
        //        return;
        //    }
        //}

        var timeStamp = DateTime.UtcNow;
        this.SetLocalState(target, setValue);

        // Publish state changed to local workers
        await this.deviceStateHub.PublishAsync(new[] {target}, cancellationToken);

        // Publish state changed to Signal API
        try
        {
            await this.signalClient.DevicesPublishStateAsync(entity.Id, target, setValue, timeStamp, cancellationToken);

            this.logger.LogDebug(
                "Device state updated - {EntityId} {Contact}: {OldValue} -> {Value}",
                target.EntityId,
                target.Name,
                currentState,
                setValue);
        }
        catch (Exception ex) when (ex.Message.Contains("IDX10223"))
        {
            this.logger.LogWarning("Failed to push device state update to Signal - Token expired. Device state {Target} to \"{Value}\"",
                target, value);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(
                ex,
                "Failed to push device state update to Signal - Unknown error. Device state {Target} to \"{Value}\"",
                target, value);
        }
    }

    private static double? ParseValueDouble(object? value)
    {
        var valueString = value?.ToString();
        if (double.TryParse(valueString, out var valueDouble))
            return valueDouble;
        return null;
    }

    private static object? ParseValue(object? value)
    {
        var valueString = value?.ToString();
        object? setValue = valueString;
        if (double.TryParse(valueString, out var valueDouble))
            setValue = valueDouble;
        else if (bool.TryParse(valueString, out var valueBool))
            setValue = valueBool;
        return setValue;
    }

    public Task<object?> GetStateAsync(ContactPointer target, CancellationToken cancellationToken = default) =>
        this.states.TryGetValue(target, out var state)
            ? Task.FromResult(state)
            : Task.FromResult<object?>(null);
}