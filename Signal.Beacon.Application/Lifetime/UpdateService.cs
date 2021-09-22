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
        private readonly ILogger<UpdateService> logger;

        public UpdateService(
            ILogger<UpdateService> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task BeginUpdateAsync(CancellationToken cancellationToken)
        {
            const string filePathExecute = "./rpi-update.sh";

            this.logger.LogDebug("Setting permission for update script...");
            const string? cmd = $"chmod +x {filePathExecute}";
            using (Process proc = Process.Start("/bin/bash", $"-c \"{cmd}\""))
                await proc.WaitForExitAsync(cancellationToken);

            var fileInfo = new FileInfo(filePathExecute);
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"sudo {fileInfo.FullName}\""
            };
            
            this.logger.LogInformation("Starting station update...");
            try
            {
                var process = Process.Start(startInfo);
                if (process == null)
                    throw new Exception("Failed to start update process.");

                _ = Task.Run(() =>
                {
                    while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        var errorLine = process.StandardError.ReadLine();
                        this.logger.LogDebug("Update ERROR > {Line}", errorLine);
                    }
                }, cancellationToken);
                
                _ = Task.Run(() =>
                {
                    while (!process.HasExited && !cancellationToken.IsCancellationRequested)
                    {
                        var outputLine = process.StandardOutput.ReadLine();
                        this.logger.LogDebug("Update INFO > {Line}", outputLine);
                    }
                }, cancellationToken);
                
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to update station");
            }
        }
    }
}