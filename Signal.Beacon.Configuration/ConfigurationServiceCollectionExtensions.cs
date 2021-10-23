using Microsoft.Extensions.DependencyInjection;
using Signal.Beacon.Core.Configuration;

namespace Signal.Beacon.Configuration;

public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddBeaconConfiguration(this IServiceCollection services)
    {
        services.AddTransient<IConfigurationService, FileSystemConfigurationService>();

        return services;
    }
}