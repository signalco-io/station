using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Application.Lifetime
{
    public class UpdateService : IUpdateService
    {
        public async Task BeginUpdateAsync(CancellationToken cancellationToken)
        {
            const string? filePathExecute = "./rpi-update.sh";
            FileInfo fileInfo = new FileInfo(filePathExecute);
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                FileName = "/bin/bash",
                Arguments = $"\"{fileInfo.FullName}\""
            };
            var process = Process.Start(startInfo);
            if (process == null)
                throw new Exception("Failed to start update process.");
            
            await process.WaitForExitAsync(cancellationToken);
        }
    }
}