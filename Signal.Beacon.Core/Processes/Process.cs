namespace Signal.Beacon.Core.Processes
{
    public record Process(
        string Type, 
        string Id, 
        string Alias, 
        bool IsDisabled, 
        string? ConfigurationSerialized,
        IProcessConfiguration? Configuration = null);
}