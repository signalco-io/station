using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Signal.Beacon.Application;
using Signal.Beacon.Application.Signal;
using Signal.Beacon.Channel.iRobot;
using Signal.Beacon.Channel.PhilipsHue;
using Signal.Beacon.Channel.Samsung;
using Signal.Beacon.Channel.Signal;
using Signal.Beacon.Channel.Tasmota;
using Signal.Beacon.Channel.Zigbee2Mqtt;
using Signal.Beacon.Configuration;
using Signal.Beacon.Core.Helpers;
using Signalco.Station.Channel.MiFlora;

namespace Signal.Beacon;

public static class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.File("Logs/log.log", rollingInterval: RollingInterval.Day,
                retainedFileTimeLimit: TimeSpan.FromDays(7))
            .WriteTo.Console()
            .CreateLogger();

        CreateHostBuilder(args).Build().Run();
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services
                    .AddHostedService<Worker>()
                    .AddBeaconConfiguration()
                    .AddBeaconApplication()
                    .AddSignalApi()
                    .AddZigbee2Mqtt()
                    .AddTasmota()
                    .AddSignal()
                    .AddPhilipsHue()
                    .AddSamsung()
                    .AddMiFlora()
                    .AddIRobot();
                //.AddVoice();

                services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));
                services.AddSingleton<IWorkerServiceManager, WorkerServiceManager>();
            })
            .UseSerilog();
}