namespace Signal.Beacon.Core.Processes
{
    public class Process
    {
        public string Type { get; set; }

        public string Id { get; set; }

        public string Alias { get; set; }

        public bool IsDisabled { get; set; }

        public string? ConfigurationSerialized { get; set; }
    }
}