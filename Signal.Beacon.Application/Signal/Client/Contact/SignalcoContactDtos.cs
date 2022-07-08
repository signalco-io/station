using System;
using System.Text.Json.Serialization;

namespace Signal.Beacon.Application.Signal.Client.Contact;

[Serializable]
public class SignalcoContactDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("valueSerialized")]
    public string? ValueSerialized { get; set; }

    [JsonPropertyName("timeStamp")]
    public DateTime TimeStamp { get; set; }
}

[Serializable]
public record SignalcoContactUpsertDto(
    [property: JsonPropertyName("entityId")] string EntityId,
    [property: JsonPropertyName("channelName")] string ChannelName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("valueSerialized")] string? ValueSerialized,
    [property: JsonPropertyName("timeStamp")] DateTime? TimeStamp);