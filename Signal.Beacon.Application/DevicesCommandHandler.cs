using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application
{
    public class DevicesCommandHandler : IDevicesCommandHandler
    {
        private readonly IDevicesDao devicesDao;
        private readonly IDeviceStateManager deviceStateManager;
        private readonly ISignalDevicesClient signalClient;
        private readonly ILogger<DevicesCommandHandler> logger;

        public DevicesCommandHandler(
            IDevicesDao devicesDao,
            IDeviceStateManager deviceStateManager,
            ISignalDevicesClient signalClient,
            ILogger<DevicesCommandHandler> logger)
        {
            this.devicesDao = devicesDao;
            this.deviceStateManager = deviceStateManager ?? throw new ArgumentNullException(nameof(deviceStateManager));
            this.signalClient = signalClient ?? throw new ArgumentNullException(nameof(signalClient));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(DeviceStateSetCommand command, CancellationToken cancellationToken)
        {
            await this.deviceStateManager.SetStateAsync(command.Target, command.Value, cancellationToken);
        }

        async Task<string> ICommandValueHandler<DeviceDiscoveredCommand, string>.HandleAsync(DeviceDiscoveredCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var device = await this.devicesDao.GetAsync(command.Identifier, cancellationToken);
                var deviceId = device?.Id;
                if (device == null || string.IsNullOrWhiteSpace(deviceId))
                {
                    deviceId = await this.signalClient.RegisterDeviceAsync(command, cancellationToken);

                    this.logger.LogInformation(
                        "New device discovered: {DeviceAlias} ({DeviceIdentifier}).",
                        command.Alias, command.Identifier);
                }
                else
                {
                    // Update info if needed
                    if (command.Alias != device.Alias ||
                        command.Manufacturer != device.Manufacturer ||
                        command.Model != device.Model)
                    {
                        await this.signalClient.UpdateDeviceInfoAsync(deviceId, command, cancellationToken);

                        this.logger.LogInformation(
                            "Updated device info: {DeviceAlias} ({DeviceIdentifier}).",
                            command.Alias, command.Identifier);
                    }
                }
                
                return deviceId;
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to discover device: {DeviceAlias} ({DeviceIdentifier})",
                    command.Alias, command.Identifier);
                throw;
            }
        }

        public async Task HandleAsync(DeviceDiscoveredCommand command, CancellationToken cancellationToken) => 
            await (this as ICommandValueHandler<DeviceDiscoveredCommand, string>).HandleAsync(command, cancellationToken);

        public async Task HandleAsync(DeviceContactUpdateCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var device = await this.devicesDao.GetByIdAsync(command.DeviceId, cancellationToken);
                if (device == null)
                    throw new Exception($"Can't update contact for device {command.DeviceId}, channel {command.ChannelName} - device not found.");

                // Get or create endpoint
                var endpoint = device.Endpoints.FirstOrDefault(e => e.Channel == command.ChannelName) ??
                               new DeviceEndpoint(command.ChannelName);

                // Ignore if not changed
                var existingContact = endpoint.Contacts.FirstOrDefault(c => c.Name == command.UpdatedContact.Name);
                if (existingContact != null && 
                    existingContact.Equals(command.UpdatedContact))
                    return;

                // Merge update contact with other contacts
                var otherContacts = endpoint.Contacts
                    .Where(c => c.Name != command.UpdatedContact.Name)
                    .ToList();
                var contacts = new List<DeviceContact>(otherContacts) 
                    { command.UpdatedContact };

                // Merge not changed endpoints with updated endpoint
                var otherEndpoints = device.Endpoints
                    .Where(e => e.Channel != command.ChannelName)
                    .ToList();
                var endpoints = new List<DeviceEndpoint>(otherEndpoints)
                    { new(command.ChannelName, contacts) };

                // Update endpoints
                await this.signalClient.UpdateDeviceEndpointsAsync(command.DeviceId, endpoints, cancellationToken);

                this.logger.LogInformation(
                    "Device contact updated successfully: {DeviceId} ({DeviceIdentifier}) | {ChannelName} {ContactName}",
                    device.Id, device.Identifier, command.ChannelName, command.UpdatedContact.Name);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, 
                    "Failed to updated device contact: {DeviceId} | {ChannelName} {ContactName}",
                    command.DeviceId, command.ChannelName, command.UpdatedContact.Name);
                throw;
            }
        }
    }
}