using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Signal.Beacon.Application.Signal.SignalR;

internal class SignalSignalRConductsHubClient : SignalSignalRHubHubClient, ISignalSignalRConductsHubClient
{
    private readonly ILogger<SignalSignalRConductsHubClient> logger;

    public SignalSignalRConductsHubClient(
        ISignalClientAuthFlow signalClientAuthFlow, 
        ILogger<SignalSignalRHubHubClient> logger,
        ILogger<SignalSignalRConductsHubClient> conductsLogger) : 
        base(signalClientAuthFlow, logger)
    {
        this.logger = conductsLogger ?? throw new ArgumentNullException(nameof(conductsLogger));
    }

    public override Task StartAsync(CancellationToken cancellationToken) => 
        this.StartAsync("conducts", cancellationToken);

    public async Task OnConductRequestMultipleAsync(Func<IEnumerable<ConductRequestDto>, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        await this.OnAsync<string>("requested-multiple", async payload =>
        {
            var requests = JsonSerializer.Deserialize<List<ConductRequestDto>>(payload);
            if (requests == null)
            {
                this.logger.LogDebug("Got empty conduct request from SignalR. Payload: {Payload}", payload);
                return;
            }

            foreach (var request in requests)
                this.logger.LogInformation(
                    "Conduct requested (multiple): {DeviceId} {ChannelName} {ContactName} {ValueSerialized} {Delay}",
                    request.DeviceId,
                    request.ChannelName,
                    request.ContactName,
                    request.ValueSerialized,
                    request.Delay);

            if (this.StartCancellationToken == null ||
                this.StartCancellationToken.Value.IsCancellationRequested)
                return;

            await handler(requests, this.StartCancellationToken.Value);
        }, cancellationToken);
    }

    public async Task OnConductRequestAsync(Func<ConductRequestDto, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        await this.OnAsync<string>("requested", async payload =>
        {
            var request = JsonSerializer.Deserialize<ConductRequestDto>(payload);
            if (request == null)
            {
                this.logger.LogDebug("Got empty conduct request from SignalR. Payload: {Payload}", payload);
                return;
            }

            this.logger.LogInformation("Conduct requested: {DeviceId} {ChannelName} {ContactName} {ValueSerialized} {Delay}",
                request.DeviceId,
                request.ChannelName,
                request.ContactName,
                request.ValueSerialized,
                request.Delay);

            if (this.StartCancellationToken == null ||
                this.StartCancellationToken.Value.IsCancellationRequested)
                return;

            await handler(request, this.StartCancellationToken.Value);
        }, cancellationToken);
    }
}