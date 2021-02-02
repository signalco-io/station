using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signal.Beacon.Api;
using Signal.Beacon.Application;
using Signal.Beacon.Application.Signal;
using Signal.Beacon.Channel.PhilipsHue;
using Signal.Beacon.Channel.Signal;
using Signal.Beacon.Channel.Tasmota;
using Signal.Beacon.Channel.Zigbee2Mqtt;
using Signal.Beacon.Configuration;
using Signal.Beacon.Core.Helpers;

namespace Signal.Beacon
{
    public static class Program
    {
        public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseKestrel(opts =>
                    {
                        opts.ListenAnyIP(5000, lo => lo.Protocols = HttpProtocols.Http1AndHttp2);
                    });

                    webBuilder.ConfigureServices(services =>
                    {
                        services
                            .AddHostedService<Worker>()
                            .AddBeaconConfiguration()
                            .AddBeaconApplication()
                            .AddSignalApi()
                            .AddZigbee2Mqtt()
                            .AddTasmota()
                            .AddSignal()
                            .AddPhilipsHue();
                            //.AddVoice();

                        services.AddTransient(typeof(Lazy<>), typeof(Lazier<>));
                    });
                });
    }
}
