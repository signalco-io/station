namespace Signal.Beacon.Application.Signal
{
    public class SignalProcessDto
    {
        public string? Type { get; }

        public string? Id { get; }

        public string? Alias { get; }

        public bool? IsDisabled { get; }

        public string? ConfigurationSerialized { get; }
    }
}