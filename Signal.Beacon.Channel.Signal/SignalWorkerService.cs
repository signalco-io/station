using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application.Mqtt;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Mqtt;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.Channel.Signal
{
    public class SignalWorkerService : IWorkerService
    {
        private const string ConfigurationFileName = "Signal.json";

        private readonly IDevicesDao devicesDao;
        private readonly IMqttClientFactory mqttClientFactory;
        private readonly IMqttDiscoveryService mqttDiscoveryService;
        private readonly IConfigurationService configurationService;
        private readonly IConductSubscriberClient conductSubscriberClient;
        private readonly ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler;
        private readonly ICommandHandler<DeviceStateSetCommand> deviceStateHandler;
        private readonly ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler;
        private readonly ILogger<SignalWorkerService> logger;
        private readonly List<IMqttClient> clients = new();

        private SignalWorkerServiceConfiguration configuration = new();
        private CancellationToken startCancellationToken;

        public SignalWorkerService(
            IDevicesDao devicesDao,
            IMqttClientFactory mqttClientFactory,
            IMqttDiscoveryService mqttDiscoveryService,
            IConfigurationService configurationService,
            IConductSubscriberClient conductSubscriberClient,
            ICommandHandler<DeviceDiscoveredCommand> deviceDiscoveryHandler,
            ICommandHandler<DeviceStateSetCommand> deviceStateHandler,
            ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler,
            ILogger<SignalWorkerService> logger)
        {
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.mqttClientFactory = mqttClientFactory ?? throw new ArgumentNullException(nameof(mqttClientFactory));
            this.mqttDiscoveryService = mqttDiscoveryService ?? throw new ArgumentNullException(nameof(mqttDiscoveryService));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.conductSubscriberClient = conductSubscriberClient ?? throw new ArgumentNullException(nameof(conductSubscriberClient));
            this.deviceDiscoveryHandler = deviceDiscoveryHandler ?? throw new ArgumentNullException(nameof(deviceDiscoveryHandler));
            this.deviceStateHandler = deviceStateHandler ?? throw new ArgumentNullException(nameof(deviceStateHandler));
            this.deviceContactUpdateHandler = deviceContactUpdateHandler ?? throw new ArgumentNullException(nameof(deviceContactUpdateHandler));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            this.configuration =
                await this.configurationService.LoadAsync<SignalWorkerServiceConfiguration>(
                    ConfigurationFileName,
                    cancellationToken);

            if (this.configuration.Servers.Any())
                foreach (var mqttServerConfig in this.configuration.Servers.ToList())
                    this.StartMqttClientAsync(mqttServerConfig);
            else
            {
                this.DiscoverMqttBrokersAsync(cancellationToken);
            }

            this.conductSubscriberClient.Subscribe(SignalChannels.DeviceChannel, this.ConductHandler);
        }

        private async Task ConductHandler(IEnumerable<Conduct> conducts, CancellationToken cancellationToken)
        {
            foreach (var conduct in conducts)
            {
                try
                {
                    var localIdentifier = conduct.Target.Identifier[7..];
                    var client = this.clients.FirstOrDefault();
                    if (client != null)
                        await client.PublishAsync($"{conduct.Target.Channel}/{localIdentifier}/{conduct.Target.Contact}/set", conduct.Value);
                }
                catch (Exception ex)
                {
                    this.logger.LogTrace(ex, "Failed to execute conduct {@Conduct}", conduct);
                    this.logger.LogWarning("Failed to execute conduct {@Conduct}", conduct);
                }
            }
        }

        private async void StartMqttClientAsync(SignalWorkerServiceConfiguration.MqttServer mqttServerConfig)
        {
            var client = this.mqttClientFactory.Create();
            await client.StartAsync("Signal.Beacon.Channel.Signal", mqttServerConfig.Url, this.startCancellationToken);
            await client.SubscribeAsync("signal/discovery/#", this.DiscoverDevicesAsync);
            this.clients.Add(client);
        }

        private async Task DiscoverDevicesAsync(MqttMessage message)
        {
            var config = JsonSerializer.Deserialize<SignalDeviceConfig>(message.Payload);
            if (config == null)
            {
                this.logger.LogWarning("Device discovery message contains invalid configuration.");
                return;
            }

            var discoveryType = message.Topic.Split("/", StringSplitOptions.RemoveEmptyEntries).Last();
            if (discoveryType == "config")
            {
                var deviceIdentifier = $"{SignalChannels.DeviceChannel}/{config.MqttTopic}";

                try
                {
                    // Signal new device discovered
                    await this.deviceDiscoveryHandler.HandleAsync(
                        new DeviceDiscoveredCommand(
                            config.Alias ?? (config.WifiHostname ?? deviceIdentifier),
                            deviceIdentifier),
                        this.startCancellationToken);

                    // Configure contacts if available in config
                    if (config.Contacts != null)
                    {
                        // Retrieve device
                        var device = await this.devicesDao.GetAsync(deviceIdentifier, this.startCancellationToken);
                        if (device == null)
                            throw new Exception($"Device not found with identifier: {deviceIdentifier}");

                        foreach (var configContact in config.Contacts)
                        {
                            try
                            {
                                if (configContact.Name != null &&
                                    configContact.DataType != null)
                                {
                                    await this.deviceContactUpdateHandler.HandleAsync(
                                        DeviceContactUpdateCommand.FromDevice(
                                            device,
                                            SignalChannels.DeviceChannel,
                                            configContact.Name,
                                            c => c with
                                            {
                                                DataType = configContact.DataType,
                                                Access = configContact.Access ?? DeviceContactAccess.None,
                                            }),
                                        this.startCancellationToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                this.logger.LogTrace(ex, "Failed to update contact.");
                                this.logger.LogWarning("Failed to update contact for contact {DeviceIdentifier} {ContactName}", deviceIdentifier, configContact.Name);
                            }
                        }
                    }

                    // Subscribe for device telemetry
                    var telemetrySubscribeTopic = $"signal/{config.MqttTopic}/#";
                    await message.Client.SubscribeAsync(telemetrySubscribeTopic,
                        msg => this.TelemetryHandlerAsync($"{SignalChannels.DeviceChannel}/{config.MqttTopic}",
                            msg));
                }
                catch (Exception ex)
                {
                    this.logger.LogTrace(ex, "Failed to configure device {Name} ({Identifier})",
                        config.WifiHostname, deviceIdentifier);
                    this.logger.LogWarning("Failed to configure device {Name} ({Identifier})",
                        config.WifiHostname, deviceIdentifier);
                }

                // Publish telemetry refresh request
                await message.Client.PublishAsync($"signal/{config.MqttTopic}/get", "get");
            }
        }

        private async Task TelemetryHandlerAsync(string deviceIdentifier, MqttMessage message)
        {
            // Check topic
            var isTelemetry = deviceIdentifier == message.Topic;
            if (!isTelemetry)
                return;

            // Check contacts available
            var telemetry = JsonSerializer.Deserialize<SignalSensorTelemetryDto>(message.Payload);
            if (telemetry?.Contacts == null)
                return;

            // Process contacts
            foreach (var telemetryContact in telemetry.Contacts)
            {
                if (telemetryContact.ContactName != null)
                {
                    await this.deviceStateHandler.HandleAsync(new DeviceStateSetCommand(
                            new DeviceTarget(SignalChannels.DeviceChannel, deviceIdentifier,
                                telemetryContact.ContactName),
                            telemetryContact.Value),
                        this.startCancellationToken);
                }
            }
        }

        private async void DiscoverMqttBrokersAsync(CancellationToken cancellationToken)
        {
            var availableBrokers =
                await this.mqttDiscoveryService.DiscoverMqttBrokerHostsAsync("signal/#", cancellationToken);
            foreach (var availableBroker in availableBrokers)
            {
                this.configuration.Servers.Add(new SignalWorkerServiceConfiguration.MqttServer
                    { Url = availableBroker.IpAddress });
                await this.configurationService.SaveAsync(ConfigurationFileName, this.configuration, cancellationToken);
                this.StartMqttClientAsync(
                    new SignalWorkerServiceConfiguration.MqttServer
                        {Url = availableBroker.IpAddress});
            }
        }
        
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var mqttClient in this.clients) 
                await mqttClient.StopAsync(cancellationToken);
        }
    }
}