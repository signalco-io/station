using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Signal.Beacon.Application.Signal.Client.Contact;

namespace Signal.Beacon.Application.Signal.Client.Entity;

[Serializable]
public record SignalcoEntityUpsertDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("alias")] string Alias);

[Serializable]
public record SignalcoEntityUpsertResponseDto(
    [property: JsonPropertyName("entityId")] string EntityId);

[Serializable]
public class SignalcoEntityDetailsDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("contacts")]
    public IEnumerable<SignalcoContactDto>? Contacts { get; set; }
}