using System;

namespace Signal.Beacon.Core.Entity;

public interface IContact
{
    string ChannelName { get; }
    
    string Name { get; }

    string? ValueSerialized { get; }
    
    DateTime? TimeStamp { get; }
}