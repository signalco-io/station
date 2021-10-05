using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Signal.Beacon.Application.Mqtt;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Mqtt;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.Channel.Zigbee2Mqtt
{
    internal class Zigbee2MqttWorkerService : IWorkerService
    {
        private const string MqttTopicSubscription = "zigbee2mqtt/#";
        private const string ConfigurationFileName = "Zigbee2mqtt.json";
        private const int MqttClientStartRetryDelay = 10000;

        private readonly IDevicesDao devicesDao;
        private readonly ICommandHandler<DeviceStateSetCommand> deviceSetStateHandler;
        private readonly ICommandHandler<DeviceDiscoveredCommand> deviceDiscoverHandler;
        private readonly ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler;
        private readonly IConductSubscriberClient conductSubscriberClient;
        private readonly IMqttClientFactory mqttClientFactory;
        private readonly IMqttDiscoveryService mqttDiscoveryService;
        private readonly IConfigurationService configurationService;
        private readonly ILogger<Zigbee2MqttWorkerService> logger;

        private readonly CancellationTokenSource cts = new();

        private readonly List<IMqttClient> clients = new();
        private Zigbee2MqttWorkerServiceConfiguration configuration = new();
        private CancellationToken startCancellationToken = CancellationToken.None;


        public Zigbee2MqttWorkerService(
            IDevicesDao devicesDao,
            ICommandHandler<DeviceStateSetCommand> devicesService,
            ICommandHandler<DeviceDiscoveredCommand> deviceDiscoverHandler,
            ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler,
            IConductSubscriberClient conductSubscriberClient,
            IMqttClientFactory mqttClientFactory,
            IMqttDiscoveryService mqttDiscoveryService,
            IConfigurationService configurationService,
            ILogger<Zigbee2MqttWorkerService> logger)
        {
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.deviceSetStateHandler = devicesService ?? throw new ArgumentNullException(nameof(devicesService));
            this.deviceDiscoverHandler = deviceDiscoverHandler ?? throw new ArgumentNullException(nameof(deviceDiscoverHandler));
            this.deviceContactUpdateHandler = deviceContactUpdateHandler ?? throw new ArgumentNullException(nameof(deviceContactUpdateHandler));
            this.conductSubscriberClient = conductSubscriberClient ?? throw new ArgumentNullException(nameof(conductSubscriberClient));
            this.mqttClientFactory = mqttClientFactory ?? throw new ArgumentNullException(nameof(mqttClientFactory));
            this.mqttDiscoveryService = mqttDiscoveryService ?? throw new ArgumentNullException(nameof(mqttDiscoveryService));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            this.configuration =
                await this.configurationService.LoadAsync<Zigbee2MqttWorkerServiceConfiguration>(
                    ConfigurationFileName,
                    cancellationToken);

            if (this.configuration.Servers.Any())
                foreach (var mqttServerConfig in this.configuration.Servers.ToList())
                    this.StartMqttClient(mqttServerConfig);
            else
            {
                _ = this.DiscoverMqttBrokersAsync(cancellationToken);
            }

            this.conductSubscriberClient.Subscribe(Zigbee2MqttChannels.DeviceChannel, this.ConductHandler);
        }

        private async Task DiscoverMqttBrokersAsync(CancellationToken cancellationToken)
        {
            try
            {
                var applicableHosts = await this.mqttDiscoveryService.DiscoverMqttBrokerHostsAsync("zigbee2mqtt/#", cancellationToken);
                foreach (var applicableHost in applicableHosts)
                {
                    try
                    {
                        // Save configuration for discovered broker
                        var config = new Zigbee2MqttWorkerServiceConfiguration.MqttServer
                        {
                            Url = applicableHost.IpAddress
                        };
                        this.configuration.Servers.Add(config);
                        await this.configurationService.SaveAsync(ConfigurationFileName, this.configuration,
                            cancellationToken);

                        // Connect to it
                        this.StartMqttClient(config);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(
                            ex,
                            "Failed to configure MQTT broker on {IpAddress}",
                            applicableHost.IpAddress);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "MQTT broker discovery failed.");
            }
        }

        private async void StartMqttClient(Zigbee2MqttWorkerServiceConfiguration.MqttServer mqttServerConfig)
        {
            try
            {
                var client = this.mqttClientFactory.Create();
                if (string.IsNullOrWhiteSpace(mqttServerConfig.Url))
                {
                    this.logger.LogWarning("MQTT Server has invalid URL: {Url}", mqttServerConfig.Url);
                    return;
                }
                
                await client.StartAsync("Signal.Beacon.Channel.Zigbee2Mqtt", mqttServerConfig.Url, this.startCancellationToken);
                await client.SubscribeAsync(
                    MqttTopicSubscription,
                    m => this.MessageHandler(m, this.cts.Token));
                await client.PublishAsync("zigbee2mqtt/bridge/config/devices/get", null);
                await client.PublishAsync("zigbee2mqtt/bridge/config/permit_join", "false");

                this.clients.Add(client);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to start MQTT client on URL: {Url}. Retry in {MqttClientStartRetryDelay}ms", mqttServerConfig.Url, MqttClientStartRetryDelay);
                await Task
                    .Delay(MqttClientStartRetryDelay, this.startCancellationToken)
                    .ContinueWith(_ => this.StartMqttClient(mqttServerConfig), this.startCancellationToken);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            this.cts.Cancel();
            await Task.WhenAll(this.clients.Select(c => c.StopAsync(cancellationToken)));
        }

        private async Task ConductHandler(IEnumerable<Conduct> conducts, CancellationToken cancellationToken)
        {
            var conductsTasks = conducts.Select(conduct =>
                this.ExecuteConductAsync(cancellationToken, conduct));
            await Task.WhenAll(conductsTasks);
        }

        private async Task ExecuteConductAsync(CancellationToken cancellationToken, Conduct conduct)
        {
            try
            {
                var device = await this.devicesDao.GetAsync(conduct.Target.Identifier, cancellationToken);
                if (device == null)
                {
                    this.logger.LogWarning("Conduct device not found. {@Conduct}", conduct);
                    return;
                }

                var contact = device.Contact(conduct.Target.Channel, conduct.Target.Contact);
                if (contact == null)
                {
                    this.logger.LogWarning("Conduct contact not found on device. {@Conduct}", conduct);
                    return;
                }

                string? value = null;
                if (contact.DataType is "enum" or "double")
                {
                    value = conduct.Value.ToString() ?? null;
                }
                else if (contact.DataType == "bool")
                {
                    value = conduct.Value.ToString()?.ToLowerInvariant() == "true" ? "ON" : "OFF";
                }

                await this.PublishStateAsync(
                    conduct.Target.Identifier,
                    conduct.Target.Contact,
                    value,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogTrace(ex, "Failed to execute conduct {@Conduct}", conduct);
                this.logger.LogWarning("Failed to execute conduct {@Conduct}", conduct);
            }
        }

        private async Task MessageHandler(MqttMessage message, CancellationToken cancellationToken)
        {
            try
            {
                var (_, topic, payload, _) = message;

                // Ignore logging
                if (topic.StartsWith("zigbee2mqtt/bridge/logging"))
                    return;
                
                if (topic == "zigbee2mqtt/bridge/devices")
                    await this.HandleDevicesConfigChangeAsync(message.Payload, cancellationToken);
                else await this.HandleDeviceTopicAsync(topic, payload, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogTrace(ex, "MessageHandler exception");
                this.logger.LogWarning("Failed to process message: {Topic} {Payload}.", message.Topic, message.Payload);
            }
        }

        private async Task HandleDeviceTopicAsync(string topic, string payload, CancellationToken cancellationToken)
        {
            // Ignore get and set requests for device
            if (topic.Contains("/set/", StringComparison.InvariantCultureIgnoreCase) ||
                topic.Contains("/get/", StringComparison.InvariantCultureIgnoreCase))
                return;

            var deviceAlias = topic.Split("/", StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Take(1)
                .FirstOrDefault();
            if (deviceAlias == null ||
                deviceAlias == "bridge")
                return;

            var device = await this.devicesDao.GetByAliasAsync(deviceAlias, cancellationToken);
            if (device == null)
            {
                this.logger.LogDebug("Device {DeviceAlias} not found", deviceAlias);
                return;
            }

            var inputs = device.Endpoints
                .SelectMany(e => e.Contacts)
                .Where(c => c.Access.HasFlag(DeviceContactAccess.Get) || c.Access.HasFlag(DeviceContactAccess.Read))
                .ToList();
            if (!inputs.Any())
            {
                this.logger.LogDebug("Device {DeviceAlias} has no inputs", deviceAlias);
                return;
            }

            // Get JSON properties (inside object)
            var jsonPayload = JToken.Parse(payload);
            var jsonPayloadObject = jsonPayload.Value<JObject>();
            var properties = jsonPayloadObject?.Properties();
            if (properties == null)
                return;

            foreach (var jProperty in properties)
            {
                var input = inputs.FirstOrDefault(i => i.Name == jProperty.Name);
                if (input == null)
                    continue;

                var target = new DeviceTarget(Zigbee2MqttChannels.DeviceChannel, device.Identifier, jProperty.Name);
                var value = jProperty.Value.Value<string>();
                var dataType = input.DataType;
                var mappedValue = MapZ2MValueToValue(dataType, value);

                // Ignore empty string values (no data)
                if (dataType != "string" &&
                    string.IsNullOrEmpty(value))
                    return;

                try
                {
                    await this.deviceSetStateHandler.HandleAsync(new DeviceStateSetCommand(target, mappedValue),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    this.logger.LogWarning(ex, "Failed to set device state {Target} to {Value}.", target, value);
                }
            }
        }

        private async Task HandleDevicesConfigChangeAsync(string messagePayload, CancellationToken cancellationToken)
        {
            var config = JsonSerializer.Deserialize<List<BridgeDevice>>(messagePayload,
                new JsonSerializerOptions {PropertyNameCaseInsensitive = true});
            if (config == null) 
                return;

            var deviceDiscoveryTasks = new List<Task>();
            foreach (var bridgeDevice in config)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(bridgeDevice.IeeeAddress))
                    {
                        this.logger.LogWarning("Invalid IEEE address {IeeeAddress}. Device skipped.",
                            bridgeDevice.IeeeAddress);
                        continue;
                    }

                    deviceDiscoveryTasks.Add(this.DeviceDiscoveredAsync(bridgeDevice, cancellationToken));
                }
                catch(Exception ex)
                {
                    this.logger.LogTrace(ex, "Device configuration failed.");
                    this.logger.LogWarning("Failed to configure device {Name} ({Address})", bridgeDevice.FriendlyName, bridgeDevice.IeeeAddress);
                }
            }

            await Task.WhenAll(deviceDiscoveryTasks);
        }
        
        private async Task DeviceDiscoveredAsync(BridgeDevice bridgeDevice, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(bridgeDevice.IeeeAddress))
                throw new ArgumentException("Device IEEE address is required.");

            // Discover device
            var deviceConfig = new DeviceDiscoveredCommand(
                bridgeDevice.FriendlyName ?? bridgeDevice.IeeeAddress,
                $"{Zigbee2MqttChannels.DeviceChannel}/{bridgeDevice.IeeeAddress}");

            if (bridgeDevice.Definition != null)
            {
                deviceConfig.Model = bridgeDevice.Definition.Model;
                deviceConfig.Manufacturer = bridgeDevice.Definition.Vendor;
            }

            await this.deviceDiscoverHandler.HandleAsync(deviceConfig, cancellationToken);

            // Retrieve discovered device
            var device = await this.devicesDao.GetAsync(deviceConfig.Identifier, cancellationToken);
            if (device == null)
            {
                this.logger.LogWarning("Failed to update device contacts because device with Identifier: {DeviceIdentifier} is not found.", deviceConfig.Identifier);
            }
            else if (bridgeDevice.Definition is { Exposes: { } })
            {
                foreach (var feature in bridgeDevice.Definition.Exposes.SelectMany(e =>
                    new List<BridgeDeviceExposeFeature>(e.Features ??
                                                        Enumerable.Empty<BridgeDeviceExposeFeature>()) { e }))
                {
                    var name = feature.Property;
                    var type = feature.Type;

                    // Must have name and type
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                        continue;

                    // Map zigbee2mqtt type to signal data type
                    var dataType = MapZ2MTypeToDataType(type);
                    if (string.IsNullOrWhiteSpace(dataType))
                    {
                        this.logger.LogWarning(
                            "Failed to map input {Input} type {Type} for device {DeviceIdentifier}",
                            name, type, deviceConfig.Identifier);
                        continue;
                    }

                    var access = DeviceContactAccess.None;
                    if (feature.Access.HasFlag(BridgeDeviceExposeFeatureAccess.Readonly))
                        access |= DeviceContactAccess.Read;
                    if (feature.Access.HasFlag(BridgeDeviceExposeFeatureAccess.Request))
                        access |= DeviceContactAccess.Get;
                    if (feature.Access.HasFlag(BridgeDeviceExposeFeatureAccess.Write))
                        access |= DeviceContactAccess.Write;

                    var dataValues = feature.Values?.ToList();

                    // Update contact basic information
                    await this.deviceContactUpdateHandler.HandleAsync(DeviceContactUpdateCommand.FromDevice(
                            device,
                            Zigbee2MqttChannels.DeviceChannel,
                            name,
                            c =>
                            {
                                var existingDataValues = new List<DeviceContactDataValue>(c.DataValues ?? Enumerable.Empty<DeviceContactDataValue>());

                                // Reassign old value labels
                                var newDataValues = dataValues?.Select(dv =>
                                                        new DeviceContactDataValue(dv,
                                                            existingDataValues.FirstOrDefault(edv => edv.Value == dv)
                                                                ?.Label)) ??
                                                    existingDataValues;

                                return c with { DataType = dataType, Access = access, DataValues = newDataValues};
                            }),
                        cancellationToken);
                }
            }

            // Refresh current states
            await this.RefreshDeviceAsync(deviceConfig.Identifier, cancellationToken);
        }

        private async Task RefreshDeviceAsync(string deviceIdentifier, CancellationToken cancellationToken)
        {
            try
            {
                // Retrieve device
                var device = await this.devicesDao.GetAsync(deviceIdentifier, cancellationToken);
                if (device == null)
                {
                    this.logger.LogDebug(
                        "Refreshing device ignored. Device {DeviceIdentifier} not found",
                        deviceIdentifier);
                    return;
                }

                // Find matching contact
                var inputContacts =
                    device.Endpoints.SelectMany(e => e.Contacts
                        .Where(i => i.Access.HasFlag(DeviceContactAccess.Get))
                        .Select(ei => ei.Name));

                // TODO: Publish only to specific client (that has device)

                var topic = $"zigbee2mqtt/{device.Alias}/get";
                var payload =
                    $"{{ {string.Join(", ", inputContacts.Select(inputContact => $"\"{inputContact}\": \"\""))} }}";

                await Task.WhenAll(this.clients.Select(c => c.PublishAsync(topic, payload)));
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to publish message.");
            }
        }

        private async Task PublishStateAsync(string deviceIdentifier, string contactName, string? value, CancellationToken cancellationToken)
        {
            try
            {
                var device = await this.devicesDao.GetAsync(deviceIdentifier, cancellationToken);
                if (device == null)
                    throw new Exception($"Device with identifier {deviceIdentifier} not found.");

                // TODO: Publish only to specific client (that has device)

                var topic = $"zigbee2mqtt/{device.Alias}/set/{contactName}";
                await Task.WhenAll(this.clients.Select(c => c.PublishAsync(topic, value)));
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to publish message.");
            }
        }

        private static object? MapZ2MValueToValue(string dataType, string? value)
        {
            return dataType switch
            {
                "bool" => ValueToBool(value),
                "double" => ValueToNumeric(value),
                "string" => value,
                _ => value
            };
        }

        private static object? ValueToNumeric(string? value) => 
            double.TryParse(value, out var doubleValue) ? doubleValue : value;

        private static object? ValueToBool(string? value) =>
            bool.TryParse(value, out var boolVal)
                ? boolVal
                : value?.ToLowerInvariant() switch
                {
                    "on" => true,
                    "off" => false,
                    _ => value
                };

        private static string? MapZ2MTypeToDataType(string type) =>
            type switch
            {
                "binary" => "bool",
                "numeric" => "double",
                "enum" => "enum",
                _ => null
            };
    }
}