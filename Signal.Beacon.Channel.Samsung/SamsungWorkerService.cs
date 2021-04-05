using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Signal.Beacon.Core.Conducts;
using Signal.Beacon.Core.Configuration;
using Signal.Beacon.Core.Network;
using Signal.Beacon.Core.Workers;
using Websocket.Client;

namespace Signal.Beacon.Channel.Samsung
{
    public class SamsungWorkerService : IWorkerService
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
        private readonly ILogger<SamsungWorkerService> logger;
        private SamsungWorkerServiceConfiguration? configuration;
        private readonly List<TvRemote> tvRemotes = new();

        public SamsungWorkerService(
            IHostInfoService hostInfoService,
            IMacLookupService macLookupService,
            IConfigurationService configurationService,
            IConductSubscriberClient conductSubscriberClient,
            ILogger<SamsungWorkerService> logger)
        {
            this.hostInfoService = hostInfoService ?? throw new ArgumentNullException(nameof(hostInfoService));
            this.macLookupService = macLookupService ?? throw new ArgumentNullException(nameof(macLookupService));
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.conductSubscriberClient = conductSubscriberClient ?? throw new ArgumentNullException(nameof(conductSubscriberClient));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            this.configuration =
                await this.configurationService.LoadAsync<SamsungWorkerServiceConfiguration>(
                    ConfigurationFileName,
                    cancellationToken);

            if (!this.configuration.TvRemotes.Any())
                _ = this.DiscoverDevices(cancellationToken);
            else this.configuration.TvRemotes.ForEach(config => this.ConnectTvAsync(config, cancellationToken));

            this.conductSubscriberClient.Subscribe(SamsungChannels.SamsungChannel, this.SamsungConductHandler);
        }

        private Task SamsungConductHandler(Conduct conduct, CancellationToken cancellationToken)
        {
            var remoteId = conduct.Target.Identifier.Replace("samsung-remote/", "");
            var matchedRemote = this.tvRemotes.FirstOrDefault();
            if (matchedRemote == null)
                throw new Exception($"No matching remote found for target {conduct.Target.Identifier}");

            if (conduct.Target.Contact == "keypress")
            {
                matchedRemote.KeyPress(conduct.Value.ToString() ??
                                       throw new ArgumentException($"Invalid conduct value ${conduct.Value}"));
            }
            else throw new ArgumentOutOfRangeException($"Unsupported contact {conduct.Target.Contact}");

            return Task.CompletedTask;
        }

        private async Task DiscoverDevices(CancellationToken cancellationToken)
        {
            var ipAddressesInRange = IPHelper.GetIPAddressesInRange(IPHelper.GetLocalIp());
            var matchedHosts =
                await this.hostInfoService.HostsAsync(ipAddressesInRange, new[] {8001}, cancellationToken);
            var hostsWithPort = matchedHosts.Where(mh => mh.OpenPorts.Count() == 1);
            foreach (var hostInfo in hostsWithPort)
            {
                if (string.IsNullOrWhiteSpace(hostInfo.PhysicalAddress))
                {
                    this.logger.LogDebug("Device MAC not found. Ip: {IpAddress}", hostInfo.IpAddress);
                    continue;
                }

                var deviceCompany =
                    await this.macLookupService.CompanyNameLookupAsync(hostInfo.PhysicalAddress, cancellationToken);
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
                var newTvRemoteConfig = new SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig(hostInfo.IpAddress);
                this.configuration?.TvRemotes.Add(newTvRemoteConfig);
                this.ConnectTvAsync(newTvRemoteConfig, cancellationToken);
            }
        }

        private void ConnectTvAsync(SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration, CancellationToken cancellationToken)
        {
            var remote = new TvRemote(configuration, this.logger);
            remote.ConnectTvAsync(cancellationToken);
            this.tvRemotes.Add(remote);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await this.configurationService.SaveAsync(ConfigurationFileName, this.configuration, cancellationToken);
        }

        private class TvRemote
        {
            public string? Id => this.configuration.Id;

            private readonly SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration;
            private readonly ILogger logger;
            private IWebsocketClient? client;
            private CancellationToken cancellationToken;


            public TvRemote(SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration, ILogger logger)
            {
                this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }

            public void GetInstalledApps()
            {
                this.client?.Send("{\"method\":\"ms.channel.emit\",\"params\":{\"event\":\"ed.installedApp.get\",\"to\":\"host\"}}");
            }

            public void KeyPress(string keyCode)
            {
                if (this.client == null)
                    throw new Exception("Client not connected.");

                var command =
                    $"{{\"method\":\"ms.remote.control\",\"params\":{{\"Cmd\":\"Click\",\"DataOfCmd\":\"{keyCode}\",\"Option\":\"false\",\"TypeOfRemote\":\"SendRemoteKey\"}}}}";
                this.client.Send(command);
            }

            public async void ConnectTvAsync(CancellationToken cancellationToken)
            {
                this.cancellationToken = cancellationToken;
                var token = this.configuration.Token;
                this.client = await this.ConnectWsEndpointAsync(this.configuration.IpAddress, 8002, "/channels/samsung.remote.control", "Signal", token);
                this.client.MessageReceived.Subscribe(this.HandleTvRemoteMessage);
            }

            private void HandleTvRemoteMessage(ResponseMessage message)
            {
                dynamic? response = JsonConvert.DeserializeObject(message.Text);

                // Acquire token procedure
                if (response?.data?.token != null)
                {
                    var token = response?.data?.token?.ToString();
                    if (string.IsNullOrWhiteSpace(token)) return;

                    // Persist acquired token
                    this.configuration.Token = token;
                    // TODO: Save config

                    // Reconnect using token
                    this.client?.Dispose();
                    this.ConnectTvAsync(this.cancellationToken);
                }
            }

            private async Task<IWebsocketClient> ConnectWsEndpointAsync(string ipAddress, int port, string url, string name, string? token)
            {
                const string wsUrl = "wss://{0}:{1}/api/v2{2}?name={3}";
                string urlFormat = wsUrl;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    urlFormat += "&token=" + token;
                }

                var client = new WebsocketClient(
                    new Uri(string.Format(urlFormat, ipAddress, port, url, Convert.ToBase64String(Encoding.UTF8.GetBytes(name)))),
                    () => new ClientWebSocket { Options = { RemoteCertificateValidationCallback = (_, _, _, _) => true } })
                {
                    ReconnectTimeout = TimeSpan.FromSeconds(30),
                    IsReconnectionEnabled = false
                };

                client.ReconnectionHappened.Subscribe(info =>
                    this.logger.LogDebug($"Reconnection happened, type: {info.Type}"));
                client.DisconnectionHappened.Subscribe(info =>
                    this.logger.LogWarning($"DisconnectionHappened, type: {info.Type}"));
                client.MessageReceived.Subscribe(msg =>
                    this.logger.LogDebug($"Message received: {msg}"));

                await client.Start();

                return client;
            }
        }
    }
}