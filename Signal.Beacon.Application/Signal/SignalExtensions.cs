﻿using Microsoft.Extensions.DependencyInjection;
using Signal.Beacon.Application.Signal.SignalR;
using Signal.Beacon.Core.Extensions;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application.Signal
{
    public static class SignalExtensions
    {
        public static IServiceCollection AddSignalApi(this IServiceCollection services)
        {
            return services
                .AddTransient<ISignalDevicesClient, SignalDevicesClient>()
                .AddTransient<ISignalBeaconClient, SignalBeaconClient>()
                .AddTransient<IStationStateService, StationStateService>()
                .AddTransient<ISignalProcessesClient, SignalProcessesClient>()
                .AddSingleton<ISignalClient, ISignalClientAuthFlow, SignalClient>()
                .AddSingleton<ISignalSignalRDevicesHubClient, SignalSignalRDevicesHubClient>()
                .AddSingleton<ISignalSignalRConductsHubClient, SignalSignalRConductsHubClient>();
        }
    }
}
