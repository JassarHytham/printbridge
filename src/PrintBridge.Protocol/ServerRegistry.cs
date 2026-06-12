using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintBridge.Protocol
{
    /// <summary>
    /// Thread-safe live table of discovered servers. Entries expire when not
    /// refreshed within the TTL, so PCs that go offline drop out of the list.
    /// </summary>
    public class ServerRegistry
    {
        private readonly TimeSpan _ttl;
        private readonly Dictionary<string, ServerEntry> _byPcId =
            new Dictionary<string, ServerEntry>();
        private readonly object _lock = new object();

        public ServerRegistry(TimeSpan ttl) => _ttl = ttl;

        public void Observe(DiscoveryBeacon beacon, string ipAddress, DateTime nowUtc)
        {
            if (beacon == null || string.IsNullOrEmpty(beacon.PcId)) return;
            lock (_lock)
            {
                _byPcId[beacon.PcId] = new ServerEntry
                {
                    PcId = beacon.PcId,
                    PcName = beacon.PcName,
                    IpAddress = ipAddress,
                    JobPort = beacon.JobPort,
                    SharedPrinters = beacon.SharedPrinters ?? new List<string>(),
                    LastSeenUtc = nowUtc
                };
            }
        }

        public List<ServerEntry> GetActive(DateTime nowUtc)
        {
            lock (_lock)
            {
                return _byPcId.Values
                    .Where(e => nowUtc - e.LastSeenUtc <= _ttl)
                    .OrderBy(e => e.PcName)
                    .ToList();
            }
        }
    }
}
