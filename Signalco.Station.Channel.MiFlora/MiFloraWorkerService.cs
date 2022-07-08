﻿using HashtagChris.DotNetBlueZ;
using HashtagChris.DotNetBlueZ.Extensions;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Shell;
using Signal.Beacon.Core.Workers;
using Tmds.DBus;

namespace Signalco.Station.Channel.MiFlora;

internal class MiFloraWorkerService : IWorkerService
{
    private readonly IDevicesDao devicesDao;
    private readonly ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler;
    private readonly ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler;
    private readonly ICommandHandler<DeviceStateSetCommand> deviceStateHandler;
    private readonly IShellService shell;
    private readonly ILogger<MiFloraWorkerService> logger;
    private static readonly SemaphoreSlim BtLock = new(1, 1);
    private Adapter? adapter;
    private readonly List<string> knownDevices = new();
    private readonly List<string> ignoredDevices = new();

    private CancellationTokenSource cts = new();
    private CancellationToken WorkerCancellationToken => this.cts.Token;

    public MiFloraWorkerService(
        IDevicesDao devicesDao,
        ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler,
        ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler,
        ICommandHandler<DeviceStateSetCommand> deviceStateHandler,
        IShellService shell,
        ILogger<MiFloraWorkerService> logger)
    {
        this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
        this.deviceDiscoveryHandler = deviceDiscoveryHandler ??
                                      throw new ArgumentNullException(nameof(deviceDiscoveryHandler));
        this.deviceContactUpdateHandler = deviceContactUpdateHandler ??
                                          throw new ArgumentNullException(nameof(deviceContactUpdateHandler));
        this.deviceStateHandler = deviceStateHandler ?? throw new ArgumentNullException(nameof(deviceStateHandler));
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
        this.cts = new CancellationTokenSource();

        _ = Task.Run(() => this.PoolDevicesLoop(this.WorkerCancellationToken), cancellationToken);

        return Task.CompletedTask;
    }

    private async Task ApplyAdapterFirmwareFixAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.logger.LogInformation("Fixing firmware directory...");
            await this.shell.ExecuteShellCommandAsync("sudo ln -s /lib/firmware /etc/firmware", cancellationToken);

        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Failed to initialize BT adapter - firmware directory fix failed");
        }

        try
        {
            this.logger.LogInformation("Attaching BCM device...");
            await this.shell.ExecuteShellCommandAsync("sudo hciattach /dev/ttyAMA0 bcm43xx 921600 noflow -",
                cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Failed to initialize BT adapter - attaching BCM adapter failed");
        }
    }

    private async Task<Adapter?> DiscoverAvailableAdapterAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.logger.LogDebug("Retrieving BT adapter...");
            return (await BlueZManager.GetAdaptersAsync().WaitAsync(TimeSpan.FromSeconds(60), cancellationToken))[0];
        }
        catch (Exception ex)
        {
            this.logger.LogTrace(ex, "Failed to assign BT adapter.");
            this.logger.LogWarning("Failed to assign BT adapter.");

            return null;
        }
    }

    private async Task AssignAdapterBtAsync(CancellationToken cancellationToken)
    {
        var newAdapter = await DiscoverAvailableAdapterAsync(cancellationToken);
        if (newAdapter == null)
        {
            // Try again after fix
            await this.ApplyAdapterFirmwareFixAsync(cancellationToken);
            newAdapter = await DiscoverAvailableAdapterAsync(cancellationToken);
            if (newAdapter == null)
                throw new Exception("No BT adapter available.");
        }

        this.logger.LogDebug("Using BT adapter: {AdapterName}", newAdapter.ObjectPath);
        this.adapter = newAdapter;
    }

    private async Task BeginDiscoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await this.AssignAdapterBtAsync(cancellationToken);
            if (this.adapter == null)
                throw new Exception("No BT adapter");

            // Start device discovery
            this.logger.LogDebug("Started discovery...");
            await this.adapter.StartDiscoveryAsync().WaitAsync(TimeSpan.FromMinutes(1), cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(ex, "Discovery start failed");
        }
    }

    private async Task PoolDevicesLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await this.BeginDiscoveryAsync(cancellationToken);
                await this.ProcessDevicesAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                await this.EndDiscoveryAsync();
            }
            catch (Exception ex)
            {
                this.logger.LogTrace(ex, "BT Pool loop failed");
                this.logger.LogDebug("BT Pool loop failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
        }
    }

    private async Task EndDiscoveryAsync()
    {
        try
        {
            if(this.adapter != null)
                await this.adapter.StopDiscoveryAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogTrace(ex, "Failed to stop discovery");
            this.logger.LogDebug("Failed to stop discovery");
        }

        try
        {
            this.adapter?.Dispose();
        }
        catch (Exception ex)
        {
            this.logger.LogTrace(ex, "Failed to dispose BT adapter");
            this.logger.LogDebug("Failed to dispose BT adapter");
        }
    }

    private async Task ProcessDevicesAsync(CancellationToken cancellationToken)
    {
        if (this.adapter == null)
            return;

        // Process known devices
        var devices = await this.adapter.GetDevicesAsync();
        foreach (var device in devices)
            await this.ProcessDeviceAsync(device, cancellationToken);
    }

    private async Task ProcessDeviceAsync(IDevice1 btDevice, CancellationToken cancellationToken)
    {
        await BtLock.WaitAsync(cancellationToken);

        try
        {
            // Skip if in ignored devices list
            if (this.ignoredDevices.Contains(btDevice.ObjectPath.ToString()))
            {
                this.logger.LogTrace("BLE Device {DevicePath} ignored", btDevice.ObjectPath);
                return;
            }

            // See if this is device we are interested info and add it to known devices list
            if (!this.knownDevices.Contains(btDevice.ObjectPath.ToString()))
            {
                // Wait for name property at most N seconds
                string? deviceName = null;
                try
                {
                    this.logger.LogDebug("BLE Device: {DevicePath} discovery...", btDevice.ObjectPath);
                    deviceName = await btDevice
                        .GetAsync<string>("Name")
                        .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                }
                catch (DBusException ex) when (ex.Message.Contains("No such property 'Name'"))
                {
                    // Ignore, name not retrieved
                }

                // Ignore if not flower care or did not respond in time
                if (string.IsNullOrWhiteSpace(deviceName) ||
                    !deviceName.Contains("Flower care"))
                {
                    this.ignoredDevices.Add(btDevice.ObjectPath.ToString());
                    this.logger.LogDebug(
                        "BLE Device {DevicePath} added to ignored devices because it didn't match: {DeviceName}",
                        btDevice.ObjectPath, deviceName);
                    return;
                }

                this.knownDevices.Add(btDevice.ObjectPath.ToString());
                this.logger.LogDebug("BLE Device {DevicePath} added to known devices", btDevice.ObjectPath);

                // Discover device
                var identifier = $"{MiFloraChannels.MiFlora}/{await btDevice.GetAddressAsync()}";
                var deviceConfig = new DeviceDiscoveredCommand(deviceName, identifier);
                await this.deviceDiscoveryHandler.HandleAsync(deviceConfig, cancellationToken);

                // Retrieve device
                var device = await this.devicesDao.GetAsync(identifier, cancellationToken);
                if (device == null)
                {
                    this.logger.LogWarning(
                        "Failed to update device contacts because device with Identifier: {DeviceIdentifier} is not found.",
                        identifier);
                }
                else
                {
                    // Discover contacts
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "temperature",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "moisture",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "light",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "conductivity",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device, MiFloraChannels.MiFlora, "battery",
                            c => c with { Access = DeviceContactAccess.Read, DataType = "double" }),
                        cancellationToken);
                }
            }

            // Skip if device is not in known devices list
            if (!this.knownDevices.Contains(btDevice.ObjectPath.ToString()))
            {
                this.logger.LogTrace("BLE Device {DevicePath} ignored", btDevice.ObjectPath);
                return;
            }

            // Retrieve device from DAO
            var deviceIdentifier = $"{MiFloraChannels.MiFlora}/{await btDevice.GetAddressAsync()}";

            try
            {
                // Try to connect
                var isConnected = await btDevice.GetConnectedAsync();
                if (!isConnected)
                {
                    await btDevice.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to connect to device {DevicePath}", btDevice.ObjectPath);
                return;
            }

            try
            {
                // Try to retrieve service
                var floraService = await btDevice
                    .GetServiceAsync("00001204-0000-1000-8000-00805f9b34fb")
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                // Force data mode
                var writeModel = await floraService.GetCharacteristicAsync("00001a00-0000-1000-8000-00805f9b34fb");
                await writeModel
                    .WriteValueAsync(new byte[] { 0xA0, 0x1F }, new Dictionary<string, object>())
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(500, cancellationToken);

                // Get sensor data characteristic
                var sensorData = await floraService
                    .GetCharacteristicAsync("00001a01-0000-1000-8000-00805f9b34fb")
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                // Read sensor data
                var sensorDataValue = await sensorData.ReadValueAsync(TimeSpan.FromSeconds(10));
                this.logger.LogTrace("Flora sensor data: {@Data}", sensorDataValue);

                // Parse sensor data
                var temperature = (short)(sensorDataValue[1] << 8 | sensorDataValue[0]) / 10d;
                var moisture = (int)sensorDataValue[7];
                var light = sensorDataValue[3] + sensorDataValue[4] * 256;
                var conductivity = sensorDataValue[8] + sensorDataValue[9] * 256;

                this.logger.LogDebug(
                    "Moisture: {MoistureValue}, Light: {LightValue}, Conductivity: {ConductivityValue}, Temperature: {TemperatureValue}",
                    moisture, light, conductivity, temperature);

                // Validate values
                if (temperature is < -30 or > 80 ||
                    moisture is < 0 or > 100 ||
                    conductivity is < 0 or > 20000)
                {
                    // TODO: Invalidate read
                    this.logger.LogDebug(
                        "Ignored values of device {DevicePath} because they are out of range",
                        btDevice.ObjectPath);
                    return;
                }

                // Set values
                await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                        new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "temperature"), temperature),
                    cancellationToken);
                await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                        new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "moisture"), moisture),
                    cancellationToken);
                await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                        new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "light"), light),
                    cancellationToken);
                await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                        new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "conductivity"), conductivity),
                    cancellationToken);

                // Get version and battery characteristic
                var versionBattery = await floraService
                    .GetCharacteristicAsync("00001a02-0000-1000-8000-00805f9b34fb")
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

                // Read version and battery data
                var versionBatteryValue = await versionBattery.ReadValueAsync(TimeSpan.FromSeconds(5));
                this.logger.LogTrace("Flora version and battery data: {@Data}", versionBatteryValue);

                // Parse version and battery data
                var battery = versionBatteryValue[0] / 255d;

                // Set value
                await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                        new DeviceTarget(MiFloraChannels.MiFlora, deviceIdentifier, "battery"), battery),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogDebug(ex, "Failed to retrieve device {DevicePath} data", btDevice.ObjectPath);
            }

            try
            {
                await btDevice.DisconnectAsync();
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to disconnect from device {DevicePath}", btDevice.ObjectPath);
            }
        }
        catch (Exception ex)
        {
            this.logger.LogDebug(
                ex,
                "Failed to process device {DevicePath}",
                btDevice.ObjectPath);
        }
        finally
        {
            BtLock.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            this.adapter?.Dispose();
        }
        catch (Exception ex)
        {
            this.logger.LogTrace(ex, "Failed to dispose BT adapter");
            this.logger.LogDebug("Failed to dispose BT adapter");
        }

        this.cts.Cancel();

        return Task.CompletedTask;
    }
}