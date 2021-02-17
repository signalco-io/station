using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Signal.Beacon.Core.Network;
using Signal.Beacon.Core.Workers;
using Websocket.Client;

namespace Signal.Beacon.Channel.Samsung
{
    public class SamsungWorkerService : IWorkerService
    {
        private readonly string[] allowedMacCompanies =
        {
            "Samsung Electronics Co.,Ltd"
        };

        private readonly IHostInfoService hostInfoService;
        private readonly IMacLookupService macLookupService;
        private readonly ILogger<SamsungWorkerService> logger;

        public SamsungWorkerService(
            IHostInfoService hostInfoService,
            IMacLookupService macLookupService,
            ILogger<SamsungWorkerService> logger)
        {
            this.hostInfoService = hostInfoService ?? throw new ArgumentNullException(nameof(hostInfoService));
            this.macLookupService = macLookupService ?? throw new ArgumentNullException(nameof(macLookupService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }


        public Task StartAsync(CancellationToken cancellationToken)
        {
            //_ = DiscoverDevices(cancellationToken);
            Task.Run(() => ConnectTvAsync("192.168.0.50", cancellationToken));

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
                if (!allowedMacCompanies.Contains(deviceCompany))
                {
                    this.logger.LogDebug(
                        "Device MAC not whitelisted. Ip: {IpAddress} Mac: {PhysicalAddress} Company: {MacCompany}",
                        hostInfo.PhysicalAddress, hostInfo.IpAddress, deviceCompany ?? "Not found");
                    continue;
                }

                // TODO: Add to possible matches
                this.logger.LogDebug("Potential Samsung TV Remote device found on address \"{DeviceIp}\"",
                    hostInfo.IpAddress);
                _ = Task.Run(() => ConnectTvAsync(hostInfo.IpAddress, cancellationToken), cancellationToken);
            }
        }

        private async Task ConnectTvAsync(string ipAddress, CancellationToken cancellationToken)
        {
            string token = null;
            var remoteClient = await ConnectWsEndpointAsync(ipAddress, 8002, "/channels/samsung.remote.control", "Signal", token);
            var artClient = await ConnectWsEndpointAsync(ipAddress, 8002, "/channels/com.samsung.art-app", "Signal", token);

            remoteClient.MessageReceived.Subscribe(async (message) =>
            {
                dynamic response = JsonConvert.DeserializeObject(message.Text);
                if (string.IsNullOrWhiteSpace(response?.data?.token)) 
                    return;

                // Reconnect using token
                token = response.data.token.ToString();
                remoteClient.Dispose();
                remoteClient = await ConnectWsEndpointAsync(
                    ipAddress, 
                    8002, 
                    "/channels/samsung.remote.control",
                    "Signal", token);
            });

            cancellationToken.WaitHandle.WaitOne();
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
                () => new ClientWebSocket {Options = {RemoteCertificateValidationCallback = (_, _, _, _) => true}})
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
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}