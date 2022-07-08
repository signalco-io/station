namespace Signal.Beacon.Core.Conditions;

public record ConditionValueDeviceState(DeviceTarget Target) : IConditionValue;