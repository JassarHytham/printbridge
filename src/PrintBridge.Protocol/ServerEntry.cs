using System;
using System.Collections.Generic;

namespace PrintBridge.Protocol
{
    public class ServerEntry
    {
        public string PcId { get; set; }
        public string PcName { get; set; }
        public string IpAddress { get; set; }
        public int JobPort { get; set; }
        public List<string> SharedPrinters { get; set; } = new List<string>();
        public DateTime LastSeenUtc { get; set; }
    }
}
