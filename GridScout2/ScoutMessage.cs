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
        public required string Version { get; init; }

        // override object.Equals
        public bool MyEquals(object? obj)
        {
            return obj is ScoutMessage message &&
            Message == message.Message &&
            Scout == message.Scout &&
            System == message.System &&
            Wormhole == message.Wormhole &&
            Entries.SequenceEqual(message.Entries) &&
            Disconnected == message.Disconnected;
        }
    }


}
