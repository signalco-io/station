using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Core.Architecture;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Network;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.Channel.Samsung
{
    internal class SamsungWorkerService : IWorkerService
    {
        private const string ConfigurationFileName = "Samsung.json";

        private readonly string[] allowedMacCompanies =
        {
            "Samsung Electronics Co.,Ltd"
        };

        private readonly IHostInfoService hostInfoService;
        private readonly IMacLookupService macLookupService;
        private readonly IConfigurationService configurationService;
        private readonly IConductSubscriberClient conductSubscriberClient;
        private readonly IDevicesDao devicesDao;
        private readonly ICommandHandler<DeviceDiscoveredCommand> discoverCommandHandler;
        private readonly ICommandHandler<DeviceStateSetCommand> stateSetCommandHandler;
        private readonly ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler;
        private readonly ILogger<SamsungWorkerService> logger;
        private SamsungWorkerServiceConfiguration? configuration;
        private readonly List<TvRemote> tvRemotes = new();
        private CancellationToken startCancellationToken;

        public SamsungWorkerService(
            IHostInfoService hostInfoService,
            IMacLookupService macLookupService,
            IConfigurationService configurationService,
            IConductSubscriberClient conductSubscriberClient,
            IDevicesDao devicesDao,
            ICommandHandler<DeviceDiscoveredCommand> discoverCommandHandler,
            ICommandHandler<DeviceStateSetCommand> stateSetCommandHandler,
            ICommandHandler<DeviceContactUpdateCommand> deviceContactUpdateHandler,
            ILogger<SamsungWorkerService> logger)
        {
            this.hostInfoService = hostInfoService ?? throw new ArgumentNullException(nameof(hostInfoService));
            this.macLookupService = macLookupService ?? throw new ArgumentNullException(nameof(macLookupService));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.conductSubscriberClient = conductSubscriberClient ?? throw new ArgumentNullException(nameof(conductSubscriberClient));
            this.devicesDao = devicesDao ?? throw new ArgumentNullException(nameof(devicesDao));
            this.discoverCommandHandler = discoverCommandHandler ?? throw new ArgumentNullException(nameof(discoverCommandHandler));
            this.stateSetCommandHandler = stateSetCommandHandler ?? throw new ArgumentNullException(nameof(stateSetCommandHandler));
            this.deviceContactUpdateHandler = deviceContactUpdateHandler ?? throw new ArgumentNullException(nameof(deviceContactUpdateHandler));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.startCancellationToken = cancellationToken;
            this.configuration =
                await this.configurationService.LoadAsync<SamsungWorkerServiceConfiguration>(
                    ConfigurationFileName,
                    cancellationToken);

            if (!this.configuration.TvRemotes.Any())
                _ = this.DiscoverDevices();
            else this.configuration.TvRemotes.ForEach(this.ConnectTv);

            this.conductSubscriberClient.Subscribe(SamsungChannels.SamsungChannel, this.SamsungConductHandler);
        }

        private Task SamsungConductHandler(Conduct conduct, CancellationToken cancellationToken)
        {
            var remoteId = conduct.Target.Identifier.Replace("samsung-remote/", "");
            var matchedRemote = this.tvRemotes.FirstOrDefault(r => r.Id == remoteId);
            if (matchedRemote == null)
                throw new Exception($"No matching remote found for target {conduct.Target.Identifier}");

            switch (conduct.Target.Contact)
            {
                case "keypress":
                    matchedRemote.KeyPress(conduct.Value.ToString() ??
                                           throw new ArgumentException($"Invalid conduct value ${conduct.Value}"));
                    break;
                case "openApp":
                    matchedRemote.OpenApp(conduct.Value.ToString() ??
                                          throw new ArgumentException($"Invalid conduct value ${conduct.Value}"));
                    break;
                case "state":
                {
                    var boolString = conduct.Value.ToString()?.ToLowerInvariant();
                    if (boolString != "true" && boolString != "false")
                        throw new Exception("Invalid contact value type. Expected boolean.");

                    // To turn on use WOL, to turn off use power key
                    if (boolString == "true")
                        matchedRemote.WakeOnLan();
                    else matchedRemote.KeyPress("KEY_POWER");
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException($"Unsupported contact {conduct.Target.Contact}");
            }

            return Task.CompletedTask;
        }

        private async Task DiscoverDevices()
        {
            var ipAddressesInRange = IpHelper.GetIPAddressesInRange(IpHelper.GetLocalIp());
            var matchedHosts = await this.hostInfoService.HostsAsync(ipAddressesInRange, new[] {8001}, this.startCancellationToken);
            foreach (var hostInfo in matchedHosts)
            {
                // Ignore if no open ports
                if (!hostInfo.OpenPorts.Any()) continue;

                if (string.IsNullOrWhiteSpace(hostInfo.PhysicalAddress))
                {
                    this.logger.LogDebug("Device MAC not found. Ip: {IpAddress}", hostInfo.IpAddress);
                    continue;
                }

                var deviceCompany =
                    await this.macLookupService.CompanyNameLookupAsync(hostInfo.PhysicalAddress, this.startCancellationToken);
                if (!this.allowedMacCompanies.Contains(deviceCompany))
                {
                    this.logger.LogDebug(
                        "Device MAC not whitelisted. Ip: {IpAddress} Mac: {PhysicalAddress} Company: {MacCompany}",
                        hostInfo.PhysicalAddress, hostInfo.IpAddress, deviceCompany ?? "Not found");
                    continue;
                }

                // TODO: Add to possible matches
                this.logger.LogDebug("Potential Samsung TV Remote device found on address \"{DeviceIp}\"",
                    hostInfo.IpAddress);

                // Try to connect
                var newTvRemoteConfig = new SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig(hostInfo.IpAddress)
                {
                    MacAddress = hostInfo.PhysicalAddress
                };
                this.configuration?.TvRemotes.Add(newTvRemoteConfig);
                this.ConnectTv(newTvRemoteConfig);
            }
        }

        private void ConnectTv(SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig remoteConfig)
        {
            var remote = new TvRemote(this.devicesDao, remoteConfig, this.logger);
            remote.OnDiscover += (_, command) =>
                this.discoverCommandHandler.HandleAsync(command, this.startCancellationToken);
            remote.OnContactUpdate += (_, command) =>
                this.deviceContactUpdateHandler.HandleAsync(command, this.startCancellationToken);
            remote.OnState += (_, command) =>
                this.stateSetCommandHandler.HandleAsync(command, this.startCancellationToken);
            remote.BeginConnectTv(this.startCancellationToken);
            this.tvRemotes.Add(remote);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.configurationService.SaveAsync(ConfigurationFileName, this.configuration, cancellationToken);
        }
    }
}