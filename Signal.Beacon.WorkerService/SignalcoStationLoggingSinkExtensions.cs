using System;
using Serilog;
using Serilog.Configuration;
using Signal.Beacon.Application.Signal;

namespace Signal.Beacon;

public static class SignalcoStationLoggingSinkExtensions
{
    public static LoggerConfiguration SignalcoStationLogging(
        this LoggerSinkConfiguration loggerConfiguration,
        Lazy<IStationStateService> stationStateService, 
        Lazy<ISignalBeaconClient> clientFactory) =>
        loggerConfiguration.Sink(new SignalcoStationLoggingSink(stationStateService, clientFactory));
}