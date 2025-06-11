using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GridScout2
{
    public record ScoutMessage
    {
        public required string Message { get; init; }
        public required string Scout { get; init; }
        public required string System { get; init; }
        public required string Wormhole { get; init; }
        public required List<ScoutEntry> Entries { get; init; }
        public bool Disconnected { get; init; }
    }
}
