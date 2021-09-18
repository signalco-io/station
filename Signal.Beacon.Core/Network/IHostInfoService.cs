﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Core.Network
{
    public interface IHostInfoService
    {
        IAsyncEnumerable<IHostInfo> HostsAsync(
            IEnumerable<string> ipAddresses,
            int[] scanPorts,
            CancellationToken cancellationToken);
    }
}
