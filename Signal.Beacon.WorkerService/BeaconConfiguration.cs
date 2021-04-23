using Signal.Beacon.Application.Auth;

namespace Signal.Beacon
{
    internal class BeaconConfiguration
    {
        public string? Identifier { get; set; }

        public AuthToken? Token { get; set; }
    }
}