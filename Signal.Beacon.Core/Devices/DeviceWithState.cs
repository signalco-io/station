using System.Collections.Generic;

namespace Signal.Beacon.Core.Devices
{
    public class DeviceWithState : DeviceConfiguration
    {
        public IEnumerable<(DeviceTarget contact, object? value)> States { get; set; }

        public DeviceWithState(string id, string alias, string identifier, IEnumerable<DeviceEndpoint>? endpoints = null) : base(id, alias, identifier, endpoints)
        {
        }
    }
}