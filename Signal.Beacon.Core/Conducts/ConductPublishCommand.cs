using System;
using System.Collections.Generic;
using Signal.Beacon.Core.Architecture;

namespace Signal.Beacon.Core.Conducts
{
    public class ConductPublishCommand : ICommand
    {
        public IEnumerable<Conduct> Conducts { get; }

        public ConductPublishCommand(IEnumerable<Conduct> conducts)
        {
            this.Conducts = conducts ?? throw new ArgumentNullException(nameof(conducts));
        }
    }
}