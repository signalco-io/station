using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application.Signal;

internal class SignalDevicesClient : ISignalDevicesClient
{
    private const string SignalApiDevicesGetUrl = "/devices";
    private const string SignalApiDevicesRegisterUrl = "/devices/register";
    private const string SignalApiDevicesEndpointsUpdateUrl = "/devices/endpoints/update";
    private const string SignalApiDevicesInfoUpdateUrl = "/devices/info/update";
    private const string SignalApiDevicesStatePublishUrl = "/devices/state";

    private readonly ISignalClient client;

    public SignalDevicesClient(
        ISignalClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IEnumerable<DeviceWithState>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var response = await this.client.GetAsync<IEnumerable<SignalDeviceDto>>(
            SignalApiDevicesGetUrl,
            cancellationToken);
        if (response == null)
            throw new Exception("Failed to retrieve devices from API.");

        return response.Select(d =>
        {
            var endpoints = MapEndpointsFromDto(d.Endpoints ?? Enumerable.Empty<SignalDeviceEndpointDto>());
            return new DeviceWithState(
                d.Id ?? throw new InvalidOperationException(),
                d.Alias ?? throw new InvalidOperationException(),
                d.DeviceIdentifier ?? throw new InvalidOperationException(),
                endpoints)
            {
                States = d.States?.Select(ds => (
                             new DeviceTarget(ds.Channel, d.DeviceIdentifier, ds.Name),
                             DeserializeValue(ds.ValueSerialized))) ??
                         new List<(DeviceTarget contact, object? value)>()
            };
        });
    }

    private static object? DeserializeValue(string? valueSerialized)
    {
        if (valueSerialized == null) return null;
        if (valueSerialized.ToLowerInvariant() == "true") return true;
        if (valueSerialized.ToLowerInvariant() == "false") return false;
        if (double.TryParse(valueSerialized, out var valueDouble))
            return valueDouble;
        return valueSerialized;
    }

    public async Task UpdateDeviceInfoAsync(string deviceId, DeviceDiscoveredCommand command, CancellationToken cancellationToken)
    {
        await this.client.PostAsJsonAsync(
            SignalApiDevicesInfoUpdateUrl,
            new SignalDeviceInfoUpdateDto(
                deviceId,
                command.Alias,
                command.Manufacturer,
                command.Model),
            cancellationToken);
    }

    public async Task UpdateDeviceEndpointsAsync(string deviceId, IEnumerable<DeviceEndpoint> endpoints, CancellationToken cancellationToken)
    {
        await this.client.PostAsJsonAsync(
            SignalApiDevicesEndpointsUpdateUrl,
            new SignalDeviceEndpointsUpdateDto(
                deviceId,
                MapEndpointsToDto(endpoints)),
            cancellationToken);
    }

    public async Task<string> RegisterDeviceAsync(DeviceDiscoveredCommand discoveredDevice, CancellationToken cancellationToken)
    {
        var response = await this.client.PostAsJsonAsync<SignalDeviceRegisterDto, SignalDeviceRegisterResponseDto>(
            SignalApiDevicesRegisterUrl,
            new SignalDeviceRegisterDto(
                discoveredDevice.Identifier,
                discoveredDevice.Alias,
                discoveredDevice.Manufacturer,
                discoveredDevice.Model),
            cancellationToken);

        if (response == null)
            throw new Exception("Didn't get valid response for device registration.");

        return response.DeviceId;
    }

    private static IEnumerable<DeviceEndpoint> MapEndpointsFromDto(
        IEnumerable<SignalDeviceEndpointDto> endpoints) =>
        endpoints.Select(e => new DeviceEndpoint(e.Channel, e.Contacts.Select(c =>
            new DeviceContact(c.Name, c.DataType, (DeviceContactAccess) c.Access)
            {
                NoiseReductionDelta = c.NoiseReductionDelta,
                DataValues = c.DataValues?.Select(dv=> new DeviceContactDataValue(dv.Value, dv.Label))
            })));

    private static IEnumerable<SignalDeviceEndpointDto> MapEndpointsToDto(IEnumerable<DeviceEndpoint> endpoints) =>
        endpoints.Select(e =>
            new SignalDeviceEndpointDto(
                e.Channel,
                e.Contacts.Select(c => new SignalDeviceEndpointContactDto(
                    c.Name,
                    c.DataType,
                    (SignalDeviceEndpointContactAccessDto)c.Access,
                    c.NoiseReductionDelta,
                    c.DataValues?.Select(dv => new SignalDeviceEndpointContactDataValueDto(dv.Value, dv.Label))))));

    public async Task DevicesPublishStateAsync(string deviceId, DeviceTarget target, object? value, DateTime timeStamp, CancellationToken cancellationToken)
    {
        var (channel, _, contact) = target;
        var data = new SignalDeviceStatePublishDto
        (
            deviceId,
            channel,
            contact,
            this.SerializeValue(value),
            timeStamp
        );

        await this.client.PostAsJsonAsync(SignalApiDevicesStatePublishUrl, data, cancellationToken);
    }
}