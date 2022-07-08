using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Application;

internal interface IDevicesCommandHandler :
    ICommandHandler<DeviceStateSetCommand>,
    ICommandHandler<DeviceDiscoveredCommand>,
    ICommandValueHandler<DeviceDiscoveredCommand, string>,
    ICommandHandler<DeviceContactUpdateCommand>
{
}