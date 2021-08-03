using System;

namespace Signal.Beacon.Application.Signal
{
    public class SignalDeviceContactStateDto
    {
        public string Name { get; set; }

        public string Channel { get; set; }

        public string? ValueSerialized { get; set; }

        public DateTime TimeStamp { get; set; }
    }
}