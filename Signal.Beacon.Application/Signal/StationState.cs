using System.Collections.Generic;

namespace Signal.Beacon.Application.Signal;

public class StationState
{
    public string Id { get; init; }

    public string Version { get; init; }

    public IEnumerable<string> RunningWorkerServices { get; init; }
}