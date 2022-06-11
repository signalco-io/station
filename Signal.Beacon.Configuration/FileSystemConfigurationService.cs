using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Signal.Beacon.Core;
using Signal.Beacon.Core.Conditions;
using Signal.Beacon.Core.Configuration;

namespace Signal.Beacon.Configuration;

public class FileSystemConfigurationService : IConfigurationService
{
    private readonly ILogger<FileSystemConfigurationService> logger;
    private readonly JsonSerializerSettings deserializationSettings = new()
    {
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Populate,
        Converters =
        {
            new BestMatchDeserializeConverter<IConditionValue>(
                typeof(ConditionValueStatic),
                typeof(ConditionValueDeviceState)),
            new BestMatchDeserializeConverter<IConditionComparable>(
                typeof(ConditionValueComparison),
                typeof(Condition))
        }
    };
    private readonly JsonSerializerSettings serializationSettings = new()
    {
        NullValueHandling = NullValueHandling.Include,
        DefaultValueHandling = DefaultValueHandling.Populate,
    };

    private readonly Dictionary<string, object> fileCache = new();
    private readonly object fsLock = new();

    public FileSystemConfigurationService(ILogger<FileSystemConfigurationService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<T> LoadAsync<T>(string name, CancellationToken cancellationToken) where T : new() =>
        await this.LoadFromFileSystemAsync<T>(name);

    public async Task SaveAsync<T>(string name, T config, CancellationToken cancellationToken) =>
        await this.SaveToFileSystemAsync(name, config, cancellationToken);

    private async Task SaveToFileSystemAsync<T>(string path, T config, CancellationToken cancellationToken)
    {
        var absolutePath = AsAbsolutePath(path);
            
        // Create directory if applicable
        if (Path.GetDirectoryName(absolutePath) is { } absolutePathDirectory)
            Directory.CreateDirectory(absolutePathDirectory);

        var configJson = JsonConvert.SerializeObject(config, this.serializationSettings);
        await File.WriteAllTextAsync(absolutePath, configJson, cancellationToken);

        lock (this.fsLock)
        {
            this.fileCache[path] = config;
        }

        this.logger.LogDebug("Saving configuration {Path}: {ConfigJson}", path, configJson);
    }

    private async Task<T> LoadFromFileSystemAsync<T>(string path)
        where T : new()
    {
        lock (this.fsLock)
        {
            if (fileCache.TryGetValue(path, out var data))
                return (T) data;

            var absolutePath = AsAbsolutePath(path);
            if (File.Exists(absolutePath))
            {
                var config = JsonConvert.DeserializeObject<T>(
                    File.ReadAllText(absolutePath),
                    this.deserializationSettings) ?? new T();
                fileCache.Add(path, config);

                logger.LogDebug("Loaded configuration from path: {AbsolutePath}: {ConfigurationJson}", absolutePath,
                    config);
                return (T) config;
            }
        }

        return new T();
    }

    private static string AsAbsolutePath(string path) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalBeacon",
            "config",
            path);
}