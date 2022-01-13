using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Core.Devices;

public record DeviceDiscoveredCommand(string Alias, string Identifier) : ICommand;