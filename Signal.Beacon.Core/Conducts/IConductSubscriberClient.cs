using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Signal.Beacon.Core.Conducts
{
    public interface IConductSubscriberClient
    {
        void Subscribe(string channel, Func<IEnumerable<Conduct>, CancellationToken, Task> handler);
    }
}