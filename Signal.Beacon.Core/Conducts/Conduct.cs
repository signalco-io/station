using Signal.Beacon.Core.Devices;

namespace Signal.Beacon.Core.Conducts;

public class Conduct
{
    public DeviceTarget Target { get; }

    public object Value { get; }

    public double Delay { get; }

    public Conduct(DeviceTarget target, object value, double delay = 0)
    {
        this.Target = target;
        this.Value = value;
        this.Delay = delay;
    }
}