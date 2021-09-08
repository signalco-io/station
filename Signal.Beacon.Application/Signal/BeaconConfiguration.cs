using Signal.Beacon.Application.Auth;

namespace Signal.Beacon.Application.Signal
{
    public class BeaconConfiguration
    {
        public string? Identifier { get; set; }

        public AuthToken? Token { get; set; }
    }
}