using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Signal.Beacon.Application.Lifetime
{
    public class UpdateService : IUpdateService
    {
        private const string FilePathExecute = "./rpi-update.sh";

        private readonly ILogger<UpdateService> logger;

        public UpdateService(
            ILogger<UpdateService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ShutdownAsync()
        {
            this.logger.LogInformation("Requested system shutdown. Executing...");
            await this.ExecuteShellCommandAsync("sudo shutdown -P now", CancellationToken.None);
        }

        public Task RestartStationAsync()
        {
            this.logger.LogInformation("Requested station restart...");

            // Exiting the application will restart the service
            Environment.Exit(0);
            return Task.CompletedTask;
        }

        public async Task UpdateSystemAsync(CancellationToken cancellationToken)
        {
            this.logger.LogInformation("Requested system update. Executing...");
            await this.ExecuteShellCommandAsync("for i in update {,dist-}upgrade auto{remove,clean}; do apt-get $i -y; done", cancellationToken);
        }

        public async Task BeginUpdateAsync(CancellationToken cancellationToken)
        {
            this.logger.LogDebug("Setting permission for update script...");
            await this.ExecuteShellCommandAsync($"chmod +x {FilePathExecute}", cancellationToken);

            this.logger.LogInformation("Starting station update...");
            var fileInfo = new FileInfo(FilePathExecute);
            await this.ExecuteShellCommandAsync($"sudo {fileInfo.FullName}", cancellationToken);
        }

        private async Task ExecuteShellCommandAsync(string command, CancellationToken cancellationToken)
        {
            using var process = new Process();
            var processRef = new WeakReference<Process>(process);
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\""
            };

            _ = Task.Run(() =>
            {
                while (processRef.TryGetTarget(out var proc) && !proc.HasExited &&
                       !cancellationToken.IsCancellationRequested)
                {
                    var errorLine = proc.StandardError.ReadLine();
                    this.logger.LogDebug("Update ERROR > {Line}", errorLine);
                }
            }, cancellationToken);

            _ = Task.Run(() =>
            {
                while (processRef.TryGetTarget(out var proc) && !proc.HasExited &&
                       !cancellationToken.IsCancellationRequested)
                {
                    var outputLine = proc.StandardOutput.ReadLine();
                    this.logger.LogDebug("Update INFO > {Line}", outputLine);
                }
            }, cancellationToken);

            process.Start();

            await process.WaitForExitAsync(cancellationToken);
        }
    }
}