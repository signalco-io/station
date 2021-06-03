﻿using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Signal.Beacon.Core.Devices;
using Signal.Beacon.Core.Network;
using Websocket.Client;

namespace Signal.Beacon.Channel.Samsung
{
    internal class TvRemote : IDisposable
    {
        public string? Id => this.configuration.Id;

        private readonly SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration;
        private readonly ILogger logger;
        private IWebsocketClient? client;
        private bool isReconnecting;
        private TvBasicInfoApiV2ResponseDto? tvBasicInfo;
        private bool isDiscovered;

        public event EventHandler<DeviceDiscoveredCommand>? OnDiscover;
        public event EventHandler<DeviceStateSetCommand>? OnState;


        public TvRemote(SamsungWorkerServiceConfiguration.SamsungTvRemoteConfig configuration, ILogger logger)
        {
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // TODO: Implement reporting available apps
        //private void GetInstalledApps()
        //{
        //    this.client?.Send("{\"method\":\"ms.channel.emit\",\"params\":{\"event\":\"ed.installedApp.get\",\"to\":\"host\"}}");
        //}

        public void KeyPress(string keyCode)
        {
            if (this.client == null)
                throw new Exception("Client not connected.");

            var command =
                $"{{\"method\":\"ms.remote.control\",\"params\":{{\"Cmd\":\"Click\",\"DataOfCmd\":\"{keyCode}\",\"Option\":\"false\",\"TypeOfRemote\":\"SendRemoteKey\"}}}}";
            this.client.Send(command);
        }

        public void WakeOnLan()
        {
            if (string.IsNullOrWhiteSpace(this.configuration.MacAddress) ||
                string.IsNullOrWhiteSpace(this.configuration.IpAddress))
                throw new Exception("MAC and IP address are required for WOL");

            IpHelper.SendWakeOnLan(
                PhysicalAddress.Parse(this.configuration.MacAddress),
                IPAddress.Parse(this.configuration.IpAddress));
        }

        public async void BeginConnectTv()
        {
            try
            {
                this.Disconnect();

                this.isReconnecting = false;

                await this.GetBasicInfoAsync();
                await this.ConnectWsRemoteAsync();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestTimeout ||
                                                  ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                this.logger.LogDebug("TV Offline {TvIp}", this.configuration.IpAddress);
                this.ReconnectAfter();
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to connect to TV {TvIp}", this.configuration.IpAddress);
                this.ReconnectAfter();
            }
        }

        private void ReportOnline(bool isOnline)
        {
            if (this.Id is null) return;

            this.OnState?.Invoke(this, new DeviceStateSetCommand(
                new DeviceTarget(SamsungChannels.SamsungChannel, this.Id, "state"),
                isOnline));
        }

        private async Task ConnectWsRemoteAsync()
        {
            this.client = await this.ConnectWsEndpointAsync(this.configuration.IpAddress, 8002,
                "/channels/samsung.remote.control", "Signal", this.configuration.Token);
            this.client.MessageReceived.Subscribe(this.HandleTvRemoteMessage);
        }

        private async Task GetBasicInfoAsync()
        {
            this.tvBasicInfo = await new HttpClient().GetFromJsonAsync<TvBasicInfoApiV2ResponseDto>(
                $"http://{this.configuration.IpAddress}:8001/api/v2/");
            if (this.tvBasicInfo?.Device != null)
                this.configuration.Id = this.tvBasicInfo.Device.Id;
        }

        private void ReconnectAfter(double delayMs = 30000)
        {
            if (this.isReconnecting) return;

            this.ReportOnline(false);

            this.Disconnect();
            Task.Delay(TimeSpan.FromMilliseconds(delayMs))
                .ContinueWith(_ => this.BeginConnectTv());
        }

        private void Disconnect()
        {
            try
            {
                this.isReconnecting = true;
                this.client?.Dispose();
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Failed to dispose the TV connection {TvIp}", this.configuration.IpAddress);
            }
        }

        private void HandleTvRemoteMessage(ResponseMessage message)
        {
            dynamic? response = JsonConvert.DeserializeObject(message.Text);

            this.ReportOnline(true);

            // Acquire token procedure
            if (response?.data?.token != null)
            {
                var token = response.data.token.ToString();
                if (string.IsNullOrWhiteSpace(token)) return;

                // Persist acquired token
                this.configuration.Token = token;

                // TODO: Save config (now instead of only on stopping)

                // Reconnect using token
                this.client?.Dispose();
                this.BeginConnectTv();
            }

            // Dispatch discovered when token is acquired
            if (!this.isDiscovered && this.configuration.Token != null && this.Id != null && this.tvBasicInfo?.Device != null)
            {
                this.OnDiscover?.Invoke(this, new DeviceDiscoveredCommand(
                    this.tvBasicInfo.Device.Name ?? this.Id,
                    this.Id)
                {
                    Manufacturer = "Samsung",
                    Model = this.tvBasicInfo.Device.ModelName,
                    Endpoints = new[]
                    {
                        new DeviceEndpoint(SamsungChannels.SamsungChannel, new []
                        {
                            new DeviceContact("state", "bool", DeviceContactAccess.Read | DeviceContactAccess.Write),
                            new DeviceContact("remote_key", "enum", DeviceContactAccess.Write)
                        })
                    }
                });

                this.isDiscovered = true;
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

            var wsClient = new WebsocketClient(
                new Uri(string.Format(urlFormat, ipAddress, port, url, Convert.ToBase64String(Encoding.UTF8.GetBytes(name)))),
                () => new ClientWebSocket { Options = { RemoteCertificateValidationCallback = (_, _, _, _) => true } })
            {
                ReconnectTimeout = TimeSpan.FromSeconds(30),
                IsReconnectionEnabled = false
            };

            wsClient.ReconnectionHappened.Subscribe(info =>
                this.logger.LogDebug("Reconnection happened {TvIp}, type: {Type}", this.configuration.IpAddress, info.Type));
            wsClient.DisconnectionHappened.Subscribe(info =>
            {
                this.logger.LogWarning("DisconnectionHappened {TvIp}, type: {Type}", this.configuration.IpAddress, info.Type);
                this.ReconnectAfter();
            });
            wsClient.MessageReceived.Subscribe(msg =>
                this.logger.LogTrace("Message received from {TvIp}: {Message}", this.configuration.IpAddress, msg.Text));

            await wsClient.Start();

            return wsClient;
        }

        [Serializable]
        private class TvBasicInfoApiV2ResponseDto
        {
            public DeviceDto? Device { get; set; }

            public record DeviceDto(string? Id, string? Name, string? ModelName);
        }

        public void Dispose()
        {
            this.Disconnect();
        }
    }
}