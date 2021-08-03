using System;
using System.Collections.Generic;

namespace Signal.Beacon.Application.Signal
{
    [Flags]
    public enum SignalDeviceEndpointContactAccessDto
    {
        None = 0x0,
        Read = 0x1,
        Write = 0x2,
        Get = 0x4
    }

    public record SignalDeviceEndpointContactDataValueDto(
        string Value,
        string Label);

    public record SignalDeviceEndpointContactDto(
        string Name,
        string DataType,
        SignalDeviceEndpointContactAccessDto Access,
        double? NoiseReductionDelta,
        IEnumerable<SignalDeviceEndpointContactDataValueDto>? DataValues);

    public record SignalDeviceEndpointDto(
        string Channel,
        IEnumerable<SignalDeviceEndpointContactDto> Contacts);

    public record SignalDeviceInfoUpdateDto(
        string DeviceId,
        string Alias,
        string? Manufacturer,
        string? Model);

    public record SignalDeviceEndpointsUpdateDto(
        string DeviceId,
        IEnumerable<SignalDeviceEndpointDto> Endpoints);

    public record SignalDeviceRegisterDto(
        string DeviceIdentifier, 
        string Alias,
        string? Manufacturer, 
        string? Model);

    public record SignalDeviceRegisterResponseDto(string DeviceId);

    public record SignalDeviceStatePublishDto(
        string DeviceId, string ChannelName, string ContactName,
        string? ValueSerialized, DateTime TimeStamp);
}