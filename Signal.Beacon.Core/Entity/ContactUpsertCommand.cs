using System;

namespace Signal.Beacon.Core.Entity;

public class ContactUpsertCommand
{
    public ContactUpsertCommand(string entityId, string channelName, string name, object? value, DateTime? timeStamp)
    {
        EntityId = entityId;
        ChannelName = channelName;
        Name = name;
        Value = value;
        TimeStamp = timeStamp;
    }

    public string EntityId { get; }
    public string ChannelName { get; }
    public string Name { get; }
    public object? Value { get; }
    public DateTime? TimeStamp { get; }
}