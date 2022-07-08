using System;

namespace Signal.Beacon.Core.Entity;

public class Contact : IContact
{
    public Contact(string channelName, string name, string? valueSerialized = null, DateTime? timeStamp = null)
    {
        ChannelName = channelName;
        Name = name;
        ValueSerialized = valueSerialized;
        TimeStamp = timeStamp;
    }

    public string ChannelName { get; }

    public string Name { get; }

    public string? ValueSerialized { get; init; }

    public DateTime? TimeStamp { get; init; }

    public static object? DeserializeValue(string? valueSerialized)
    {
        if (valueSerialized == null) return null;
        if (valueSerialized.ToLowerInvariant() == "true") return true;
        if (valueSerialized.ToLowerInvariant() == "false") return false;
        if (double.TryParse(valueSerialized, out var valueDouble))
            return valueDouble;
        return valueSerialized;
    }
}