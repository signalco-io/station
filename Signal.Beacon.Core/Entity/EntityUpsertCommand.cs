using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Core.Entity;

public record EntityUpsertCommand(string Alias) : ICommand;