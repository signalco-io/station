using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signal.Beacon.Application;
using Signal.Beacon.Application.Signal;
using Signal.Beacon.Channel.PhilipsHue;
using Signal.Beacon.Channel.Samsung;
using Signal.Beacon.Channel.Signal;
using Signal.Beacon.Channel.Tasmota;
using Signal.Beacon.Channel.Zigbee2Mqtt;
using Signal.Beacon.Configuration;
using Signal.Beacon.Core.Helpers;
using Signal.Beacon.Voice;

namespace Signal.Beacon
{
    public static class Program
    {
        public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

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
                        .AddVoice();

                    services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));
                });
    }
}
