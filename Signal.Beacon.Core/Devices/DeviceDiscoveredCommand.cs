using System;
using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Core.Devices
{
    public class DeviceDiscoveredCommand : ICommand
    {
        public string Alias { get; }
        
        public string Identifier { get; }

        public string? Model { get; set; }

        public string? Manufacturer { get; set; }

        public DeviceDiscoveredCommand(string alias, string identifier)
        {
            this.Alias = alias ?? throw new ArgumentNullException(nameof(alias));
            this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        }
    }
}