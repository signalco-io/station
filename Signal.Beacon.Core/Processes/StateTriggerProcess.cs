using System.Collections.Generic;
using Signal.Beacon.Core.Conditions;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Devices;

namespace Signal.Beacon.Core.Processes
{
    public class StateTriggerProcess
    {
        public string Alias { get; set; }

        public bool IsDisabled { get; set; }

        public double Delay { get; set; }

        public IEnumerable<DeviceTarget> Triggers { get; set; }

        public Condition Condition { get; set; }

        public IEnumerable<Conduct> Conducts { get; set; }

        public string Id { get; set; }
    }
}