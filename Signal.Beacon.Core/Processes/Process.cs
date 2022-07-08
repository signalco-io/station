namespace Signal.Beacon.Core.Processes;

public record Process(
    string Id, 
    string Alias,
    IProcessConfiguration? Configuration) : IProcess;