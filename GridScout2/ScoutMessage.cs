using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GridScout2
{
    public class ScoutMessage
    {
        public required string Message { get; set; }
        public required string Scout { get; set; }
        public required string Wormhole { get; set; }
    }
}
