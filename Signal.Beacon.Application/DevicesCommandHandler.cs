using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        public async Task HandleAsync(DeviceDiscoveredCommand command, CancellationToken cancellationToken)
        {
            this.logger.LogDebug(
                "New device discovered: {DeviceAlias} ({DeviceIdentifier}).",
                command.Alias, command.Identifier);

            try
            {
                var device = await this.devicesDao.GetAsync(command.Identifier, cancellationToken);
                var deviceId = device?.Id;
                if (device == null || string.IsNullOrWhiteSpace(deviceId))
                    deviceId = await this.signalClient.RegisterDeviceAsync(command, cancellationToken);
                else
                {
                    // Update info if needed
                    if (command.Alias != device.Alias ||
                        command.Manufacturer != device.Manufacturer ||
                        command.Model != device.Model)
                        await this.signalClient.UpdateDeviceInfoAsync(deviceId, command, cancellationToken);

                    // Update endpoints
                    await this.signalClient.UpdateDeviceEndpointsAsync(deviceId, command, cancellationToken);
                }

                // Update locally
                await this.devicesDao.UpdateDeviceAsync(
                    command.Identifier,
                    new DeviceConfiguration(
                        deviceId,
                        command.Alias,
                        command.Identifier,
                        command.Endpoints)
                    {
                        Manufacturer = command.Manufacturer,
                        Model = command.Model
                    }, cancellationToken);

                this.logger.LogInformation(
                    "Device discovered successfully: {DeviceAlias} ({DeviceIdentifier})",
                    command.Alias, command.Identifier);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to discover device: {DeviceAlias} ({DeviceIdentifier})",
                    command.Alias, command.Identifier);
                throw;
            }
        }
    }
}