using System;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Signal.Beacon.Application.Conducts;
using Signal.Beacon.Application.Processing;
using Signal.Beacon.Application.Signal.SignalR;
using Signal.Beacon.Core.Workers;

namespace Signal.Beacon.Application
{
    internal class MqttServer
    {
        private IMqttServer? mqttServer;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            const int port = 1883;
            var optionsBuilder = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .WithConnectionValidator(
                    c =>
                    {
                        // TODO: Do auth
                        //var currentUser = config.Users.FirstOrDefault(u => u.UserName == c.Username);

                        //if (currentUser == null)
                        //{
                        //    c.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        //    return;
                        //}

                        //if (c.Username != currentUser.UserName)
                        //{
                        //    c.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        //    return;
                        //}

                        //if (c.Password != currentUser.Password)
                        //{
                        //    c.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                        //    return;
                        //}

                        c.ReasonCode = MqttConnectReasonCode.Success;
                    })
                .WithSubscriptionInterceptor(c => c.AcceptSubscription = true)
                .WithApplicationMessageInterceptor(c => c.AcceptPublish = true);

            this.mqttServer = new MqttFactory().CreateMqttServer();
            await this.mqttServer.StartAsync(optionsBuilder.Build());

            cancellationToken.Register(this.OnCancelled);
        }

        private async void OnCancelled()
        {
            try
            {
                if (this.mqttServer != null) 
                    await this.mqttServer.StopAsync();
            }
            catch
            {
                // TODO: Log error stopping server
            }
        }
    }

    internal class ApplicationWorkerService : IWorkerService
    {
        private readonly IProcessor processor;
        private readonly ISignalSignalRDevicesHubClient devicesHubClient;
        private readonly ISignalSignalRConductsHubClient conductsHubClient;
        private readonly IConductManager conductManager;
        private MqttServer? mqttServer;

        public ApplicationWorkerService(
            IProcessor processor,
            ISignalSignalRDevicesHubClient devicesHubClient,
            ISignalSignalRConductsHubClient conductsHubClient,
            IConductManager conductManager)
        {
            this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
            this.devicesHubClient = devicesHubClient ?? throw new ArgumentNullException(nameof(devicesHubClient));
            this.conductsHubClient = conductsHubClient ?? throw new ArgumentNullException(nameof(conductsHubClient));
            this.conductManager = conductManager ?? throw new ArgumentNullException(nameof(conductManager));
        }
        
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _ = this.devicesHubClient.StartAsync(cancellationToken);
            _ = this.conductsHubClient.StartAsync(cancellationToken);
            await this.processor.StartAsync(cancellationToken);
            await this.conductManager.StartAsync(cancellationToken);

            //this.mqttServer = new MqttServer();
            //await this.mqttServer.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}