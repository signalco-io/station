﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Signal.Beacon.Application.Signal.SignalR
{
    internal abstract class SignalSignalRHubHubClient
    {
        protected CancellationToken? StartCancellationToken;

        private readonly ISignalClientAuthFlow signalClientAuthFlow;
        private readonly ILogger<SignalSignalRHubHubClient> logger;
        private HubConnection? connection;
        private readonly object startLock = new();
        private bool isStarted;

        protected SignalSignalRHubHubClient(
            ISignalClientAuthFlow signalClientAuthFlow,
            ILogger<SignalSignalRHubHubClient> logger)
        {
            this.signalClientAuthFlow = signalClientAuthFlow ?? throw new ArgumentNullException(nameof(signalClientAuthFlow));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected async Task OnAsync<T>(string targetName, Func<T, Task> arg, CancellationToken cancellationToken)
        {
            await this.StartAsync(cancellationToken);
            this.connection.On(targetName, arg);
        }

        public abstract Task StartAsync(CancellationToken cancellationToken);

        protected async Task StartAsync(string hubName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(hubName))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(hubName));

            // Already started check
            if (this.isStarted) 
                return;

            // Locked start flag
            lock (this.startLock)
            {
                if (this.isStarted) 
                    return;

                this.isStarted = true;
            }

            this.StartCancellationToken = cancellationToken;

            try
            {
                this.connection = new HubConnectionBuilder()
                    .AddJsonProtocol()
                    .WithUrl($"https://signal-api.azurewebsites.net/api/signalr/{hubName}", options =>
                    {
                        options.AccessTokenProvider = async () =>
                        {
                            var retryCount = 0;
                            while (!this.StartCancellationToken.Value.IsCancellationRequested)
                            {
                                if (await this.signalClientAuthFlow.GetTokenAsync(this.StartCancellationToken.Value) !=
                                    null)
                                    break;

                                // Abort after some time
                                if (retryCount > 1000) 
                                    return null;

                                // Incremental Delay 
                                var delay = ++retryCount * 1000;
                                this.logger.LogWarning("SignalR token failed to retrieve... delayed retry in {DelayMs}ms", delay);
                                await Task.Delay(delay, this.StartCancellationToken.Value);
                            }

                            var tokenResult = await this.signalClientAuthFlow.GetTokenAsync(this.StartCancellationToken.Value);
                            return tokenResult?.AccessToken;
                        };
                    })
                    .WithAutomaticReconnect()
                    .Build();

                this.connection.Reconnecting += error => {
                    this.logger.LogInformation(error, "{HubName} hub connection - reconnecting...", hubName);
                    return Task.CompletedTask;
                };

                this.connection.Reconnected += _ => {
                    this.logger.LogInformation("{HubName} hub connection reconnected", hubName);
                    return Task.CompletedTask;
                };

                this.connection.Closed += async error => {
                    this.logger.LogInformation(error, "{HubName} hub connection closed", hubName);
                    await this.ReconnectDelayedAsync(hubName, cancellationToken);
                };

                // Start the connection
                await this.connection.StartAsync(this.StartCancellationToken.Value);

                this.logger.LogInformation("{HubName} hub started", hubName);
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, $"Failed to start Signal SignalR {hubName} hub");

                await this.ReconnectDelayedAsync(hubName, cancellationToken);
            }
        }

        private async Task ReconnectDelayedAsync(string hubName, CancellationToken cancellationToken)
        {
            this.isStarted = false;
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            _ = this.StartAsync(hubName, cancellationToken);
        }
    }
}