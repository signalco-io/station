namespace Signal.Beacon.Core.Processes;

public interface IProcess
{
    string Id { get; init; }
    string Alias { get; init; }
    IProcessConfiguration? Configuration { get; init; }
}