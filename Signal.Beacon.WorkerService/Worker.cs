using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Signal.Beacon.Application;
using Signal.Beacon.Application.Auth;
using Signal.Beacon.Application.Auth0;
using Signal.Beacon.Application.Signal.Client;
using Signal.Beacon.Application.Signal.Client.Station;
using Signal.Beacon.Application.Signal.Station;
using Signal.Beacon.Core.Configuration;

namespace Signal.Beacon;

public class Worker : BackgroundService
{
    private readonly ISignalcoStationClient signalClient;
    private readonly ISignalcoClientAuthFlow signalcoClientAuthFlow;
    private readonly IConfigurationService configurationService;
    private readonly IWorkerServiceManager workerServiceManager;
    private readonly IStationStateManager stationStateManager;
    private readonly ILogger<Worker> logger;

    public Worker(
        ISignalcoStationClient signalClient,
        ISignalcoClientAuthFlow signalcoClientAuthFlow,
        IConfigurationService configurationService,
        IWorkerServiceManager workerServiceManager,
        IStationStateManager stationStateManager,
        ILogger<Worker> logger)
    {
        this.signalClient = signalClient ?? throw new ArgumentNullException(nameof(signalClient));
        this.signalcoClientAuthFlow = signalcoClientAuthFlow ?? throw new ArgumentNullException(nameof(signalcoClientAuthFlow));
        this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        this.workerServiceManager = workerServiceManager ?? throw new ArgumentNullException(nameof(workerServiceManager));
        this.stationStateManager = stationStateManager;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Load configuration
        var config = await this.configurationService.LoadAsync<StationConfiguration>("beacon.json", stoppingToken);
        if (config.Token == null || config.Identifier == null)
        {
            this.logger.LogInformation("Beacon not registered. Started registration...");
                
            try
            {
                // Assign identifier to Beacon
                if (string.IsNullOrWhiteSpace(config.Identifier))
                {
                    config.Identifier = Guid.NewGuid().ToString();
                    await this.configurationService.SaveAsync("beacon.json", config, stoppingToken);
                }

                // Authorize Beacon
                var deviceCodeResponse = await new Auth0DeviceAuthorization().GetDeviceCodeAsync(stoppingToken);
                this.logger.LogInformation("Device auth: {Response}",
                    JsonSerializer.Serialize(deviceCodeResponse));
                    
                // TODO: Post device flow request to user (CTA)
                    
                var token = await new Auth0DeviceAuthorization().WaitTokenAsync(deviceCodeResponse, stoppingToken);
                if (token == null)
                    throw new Exception("Token response not received");
                this.logger.LogInformation("Authorized successfully");

                // Register Beacon
                this.signalcoClientAuthFlow.AssignToken(token);
                await this.signalClient.RegisterBeaconAsync(config.Identifier, stoppingToken);
                this.logger.LogInformation("Registered successfully");

                // Persist token
                config.Token = token;
                await this.configurationService.SaveAsync("beacon.json", config, stoppingToken);
                this.logger.LogInformation("Token saved");
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to register Beacon. Some functionality will be limited.");
            }
        }
        else
        {
            this.signalcoClientAuthFlow.AssignToken(config.Token);
        }

        this.signalcoClientAuthFlow.OnTokenRefreshed += this.SignalcoClientAuthFlowOnOnTokenRefreshed;

        // Start state reporting
        await this.stationStateManager.BeginMonitoringStateAsync(stoppingToken);

        // Start worker services
        await this.workerServiceManager.StartAllWorkerServicesAsync(stoppingToken);

        // Wait for cancellation token
        while (!stoppingToken.IsCancellationRequested)
            await Task.WhenAny(Task.Delay(-1, stoppingToken));

        // Stop services
        await this.workerServiceManager.StopAllWorkerServicesAsync();
    }

    private async void SignalcoClientAuthFlowOnOnTokenRefreshed(object? sender, AuthToken? e)
    {
        try
        {
            var config =
                await this.configurationService.LoadAsync<StationConfiguration>("Beacon.json",
                    CancellationToken.None);
            config.Token = e;
            await this.configurationService.SaveAsync("Beacon.json", config, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Failed to persist refreshed token.");
        }
    }
}