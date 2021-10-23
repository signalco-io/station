namespace Signal.Beacon.Application.Signal;

public class SignalProcessDto
{
    public string? Type { get; set; }

    public string? Id { get; set; }

    public string? Alias { get; set; }

    public bool? IsDisabled { get; set; }

    public string? ConfigurationSerialized { get; set; }
}