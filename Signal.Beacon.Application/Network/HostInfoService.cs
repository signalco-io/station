using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Signal.Beacon.Core.Network;

namespace Signal.Beacon.Application.Network
{
    public class HostInfoService : IHostInfoService
    {
        public async Task<IEnumerable<IHostInfo>> HostsAsync(
            IEnumerable<string> ipAddresses,
            int[] scanPorts,
            CancellationToken cancellationToken)
        {
            var arpResult = await ArpLookupAsync();
            var pingResults = await Task
                .WhenAll(ipAddresses.Select(address =>
                {
                    var arpLookupResult = arpResult.FirstOrDefault(a => a.ip == address);
                    return GetHostInformationAsync(
                        address,
                        scanPorts,
                        arpLookupResult.physical,
                        cancellationToken);
                }))
                .ConfigureAwait(false);
            return pingResults.Where(i => i != null).Select(i => i!);
        }

        private static async Task<HostInfo?> GetHostInformationAsync(
            string address, 
            IEnumerable<int> applicablePorts, 
            string? arpLookupPhysical,
            CancellationToken cancellationToken)
        {
            var ping = await PingIpAddressAsync(address, cancellationToken);
            if (ping == null)
                return null;

            var portPing = Math.Min(2000, Math.Max(100, ping.Value * 2)); // Adaptive port connection timeout based on ping value
            var openPorts = (await OpenPortsAsync(address, applicablePorts, TimeSpan.FromMilliseconds(portPing))).ToList();

            return new HostInfo(address, ping.Value)
            {
                OpenPorts = openPorts,
                PhysicalAddress = arpLookupPhysical
            };
        }

        private static async Task<IEnumerable<(string ip, string physical)>> ArpLookupAsync()
        {
            try
            {
                System.Diagnostics.Process pProcess = new()
                {
                    StartInfo =
                    {
                        FileName = "arp",
                        Arguments = "-a ",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                pProcess.Start();
                string cmdOutput = await pProcess.StandardOutput.ReadToEndAsync();

                // Regex supports following outputs:
                // Windows (10): 192.168.0.1           00-00-00-00-00-00     dynamic
                // Ubuntu (20):  HOSTNAME (192.168.0.1) at 00:00:00:00:00:00 [ether] on eth0
                const string pattern = @"\(*(?<ip>([0-9]{1,3}\.?){4})\)*\s*(at)*\s*(?<mac>([a-f0-9]{2}(-|:)?){6})";
                var pairs = new List<(string ip, string physical)>();
                foreach (Match m in Regex.Matches(cmdOutput, pattern, RegexOptions.IgnoreCase))
                {
                    pairs.Add(
                        (
                            m.Groups["ip"].Value,
                            m.Groups["mac"].Value.Replace("-", ":")
                        ));
                }

                return pairs;
            }
            catch
            {
                return Enumerable.Empty<(string ip, string physical)>();
            }
        }

        private static async Task<long?> PingIpAddressAsync(string address, CancellationToken cancellationToken, int timeout = 1000, int retry = 2)
        {
            using var ping = new Ping();
            var tryCount = 0;

            while (tryCount++ < retry && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = (await ping.SendPingAsync(address, timeout).ConfigureAwait(false));
                    if (result.Status == IPStatus.Success)
                        return result.RoundtripTime;
                }
                catch
                {
                    // Do nothing
                }
            }

            return null;
        }

        private static async Task<IEnumerable<int>> OpenPortsAsync(string host, IEnumerable<int> ports, TimeSpan timeout)
        {
            var tasks = ports.Select(port => Task.Run(() =>
            {
                try
                {
                    using var client = new TcpClient();
                    var result = client.BeginConnect(host, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(timeout);
                    client.EndConnect(result);
                    return (Port: port, Open: success);
                }
                catch
                {
                    return (Port: port, Open: false);
                }
            }));

            var openPorts = await Task.WhenAll(tasks);
            return openPorts.Where(p => p.Open).Select(p => p.Port);
        }
    }
}