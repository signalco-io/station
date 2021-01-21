using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Processes;
using Signal.Beacon.Core.Signal;

namespace Signal.Beacon.Application.Signal
{
    internal class SignalProcessesClient : ISignalProcessesClient
    {
        private const string SignalApiProcessesGetUrl = "/processes";
        
        private readonly ISignalClient client;

        public SignalProcessesClient(
            ISignalClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }
        

        public async Task<IEnumerable<Process>> GetProcessesAsync(CancellationToken cancellationToken)
        {
            var response = await this.client.GetAsync<IEnumerable<SignalProcessDto>>(
                SignalApiProcessesGetUrl,
                cancellationToken);
            if (response == null)
                throw new Exception("Failed to retrieve processes from API.");

            return response.Select(p => new Process
            {
                Id = p.Id ?? throw new Exception("Got process with no ID."),
                IsDisabled = p.IsDisabled ?? false,
                Alias = p.Alias ?? throw new Exception("Got process with no Alias."),
                Type = p.Type ?? throw new Exception("Got process with no Type."),
                ConfigurationSerialized = p.ConfigurationSerialized
            });
        }
    }
}