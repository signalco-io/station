﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Signal.Beacon.Application.Signal.SignalR;

internal class SignalSignalRDevicesHubClient : SignalSignalRHubHubClient, ISignalSignalRDevicesHubClient
{
    public SignalSignalRDevicesHubClient(
        ISignalClientAuthFlow signalClientAuthFlow, 
        ILogger<SignalSignalRHubHubClient> logger) : 
        base(signalClientAuthFlow, logger)
    {
    }

    public override Task StartAsync(CancellationToken cancellationToken) => 
        this.StartAsync("devices", cancellationToken);

    public void OnDeviceState(Func<SignalDeviceStatePublishDto, CancellationToken, Task> handler, CancellationToken cancellationToken) => 
        this.On<SignalDeviceStatePublishDto>("devicestate", async state => await handler(state, cancellationToken), cancellationToken);
}