using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application.PubSub;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Extensions;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application
{
    public class DeviceStateManager : IDeviceStateManager
    {
        private readonly IPubSubHub<DeviceTarget> deviceStateHub;
        private readonly ISignalDevicesClient signalClient;
        private readonly IDevicesDao devicesDao;
        private readonly ILogger<DeviceStateManager> logger;
        private readonly ConcurrentDictionary<DeviceTarget, object?> states = new();


        public DeviceStateManager(
            ISignalDevicesClient signalClient,
            IDevicesDao devicesDao,
            IPubSubHub<DeviceTarget> deviceStateHub,
            ILogger<DeviceStateManager> logger)
        {
            this.deviceStateHub = deviceStateHub ?? throw new ArgumentNullException(nameof(deviceStateHub));
            this.signalClient = signalClient ?? throw new ArgumentNullException(nameof(signalClient));
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public IDisposable Subscribe(Func<DeviceTarget, CancellationToken, Task> handler) => 
            this.deviceStateHub.Subscribe(this, handler);

        internal void SetLocalState(DeviceTarget target, object? value) => this.states.AddOrSet(target, value);

        public async Task SetStateAsync(DeviceTarget target, object? value, CancellationToken cancellationToken)
        {
            var setValue = ParseValue(value);

            // Retrieve device
            var device = await this.devicesDao.GetAsync(target.Identifier, cancellationToken);
            if (device == null)
            {
                this.logger.LogDebug("Device with identifier not found {DeviceId} {Contact}: {Value}. State ignored",
                    target.Identifier,
                    target.Contact,
                    setValue);
                return;
            }

            // Retrieve contact
            var contact = await this.devicesDao.GetInputContactAsync(target, cancellationToken);
            if (contact == null)
            {
                this.logger.LogTrace(
                    "Device contact not found {DeviceId} {Contact}: {Value}. State ignored",
                    target.Identifier,
                    target.Contact,
                    setValue);
                return;
            }

            // Ignore if value didnt change, dont ignore for actions
            var currentState = ParseValue(await this.GetStateAsync(target));
            var oldAndNewNull = currentState == null && setValue == null;
            var isActionOrString = contact.DataType == "action" || contact.DataType == "string";
            var areEqual = currentState?.Equals(setValue) ?? false;
            var areEqualValues = currentState == setValue;
            if (oldAndNewNull || 
                !isActionOrString && (areEqual || areEqualValues))
            {
                this.logger.LogTrace(
                    "Device state ignore because it didn't change. {DeviceId} {Contact}: {Value}",
                    target.Identifier, 
                    target.Contact,
                    setValue);
                return;
            }

            // Apply noise reducing delta
            if (contact.DataType == "double" && 
                contact.NoiseReductionDelta.HasValue)
            {
                var currentValueDouble = ParseValueDouble(currentState);
                var setValueDouble = ParseValueDouble(setValue);
                if (currentValueDouble != null &&
                    setValueDouble != null &&
                    Math.Abs(currentValueDouble.Value - setValueDouble.Value) <= contact.NoiseReductionDelta.Value)
                {
                    this.logger.LogTrace(
                        "Device contact noise reduction threshold not reached. State ignored. {DeviceId} {Contact}: {Value}",
                        target.Identifier,
                        target.Contact,
                        setValue);
                    return;
                }
            }

            var timeStamp = DateTime.UtcNow;
            this.SetLocalState(target, setValue);

            // Publish state changed to local workers
            await this.deviceStateHub.PublishAsync(new[] {target}, cancellationToken);

            // Publish state changed to Signal API
            try
            {
                await this.signalClient.DevicesPublishStateAsync(device.Id, target, setValue, timeStamp, cancellationToken);

                this.logger.LogDebug(
                    "Device state updated - {DeviceId} {Contact}: {OldValue} -> {Value}",
                    target.Identifier,
                    target.Contact,
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

        public Task<object?> GetStateAsync(DeviceTarget target) =>
            this.states.TryGetValue(target, out var state)
                ? Task.FromResult(state)
                : Task.FromResult<object?>(null);
    }
}