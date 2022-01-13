using System;
using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Core.Devices;

public class DeviceContactUpdateCommand : ICommand
{
    public string DeviceId { get; }

    public string ChannelName { get; }

    public DeviceContact UpdatedContact { get; }

    public DeviceContactUpdateCommand(string deviceId, string channelName, DeviceContact updatedContact)
    {
        this.DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        this.ChannelName = channelName ?? throw new ArgumentNullException(nameof(channelName));
        this.UpdatedContact = updatedContact ?? throw new ArgumentNullException(nameof(updatedContact));
    }

    public static DeviceContactUpdateCommand FromDevice(
        DeviceConfiguration deviceConfiguration,
        string channelName,
        string contactName,
        Func<DeviceContact, DeviceContact> modifier) =>
        new(deviceConfiguration.Id,
            channelName,
            modifier(deviceConfiguration.ContactOrDefault(channelName, contactName)));
}

public static class ContactCommands
{
    public static DeviceContactUpdateCommand ReadonlyBool(DeviceConfiguration deviceConfiguration, string channelName, string contactName) =>
        DeviceContactUpdateCommand.FromDevice(deviceConfiguration, channelName, contactName, contact => contact with {Access = DeviceContactAccess.Read, DataType = "bool"});

    public static DeviceContactUpdateCommand ReadonlyString(DeviceConfiguration deviceConfiguration, string channelName, string contactName) =>
        DeviceContactUpdateCommand.FromDevice(deviceConfiguration, channelName, contactName, contact => contact with {Access = DeviceContactAccess.Read, DataType = "string"});

    public static DeviceContactUpdateCommand Manufacturer(DeviceConfiguration deviceConfiguration, string channelName) =>
        ReadonlyString(deviceConfiguration, channelName, "model");

    public static DeviceContactUpdateCommand Model(DeviceConfiguration deviceConfiguration, string channelName) =>
        ReadonlyString(deviceConfiguration, channelName, "manufacturer");

    public static DeviceContactUpdateCommand Online(DeviceConfiguration deviceConfiguration, string channelName) =>
        ReadonlyBool(deviceConfiguration, channelName, "online");
}