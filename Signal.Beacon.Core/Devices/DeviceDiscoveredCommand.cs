using System;
using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Core.Devices;

public record DeviceDiscoveredCommand(string Alias, string Identifier) : ICommand
{
    public string? Model { get; set; }

    public string? Manufacturer { get; set; }
}