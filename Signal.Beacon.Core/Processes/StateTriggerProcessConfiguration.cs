using System.Collections.Generic;
using Signal.Beacon.Core.Conditions;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Devices;

namespace Signal.Beacon.Core.Processes
{
    public record StateTriggerProcessConfiguration(
        double Delay = 0, 
        IEnumerable<DeviceTarget>? Triggers = null, 
        Condition? Condition = null,
        IEnumerable<Conduct>? Conducts = null) : IProcessConfiguration;
}