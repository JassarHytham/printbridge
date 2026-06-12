using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PrintBridge.Protocol;

namespace PrintBridge.App
{
    /// <summary>The "using" half: listens for beacons and maintains a live registry.</summary>
    public class DiscoveryService : IDisposable
    {
        private readonly ServerRegistry _registry =
            new ServerRegistry(TimeSpan.FromSeconds(10));
        private UdpClient _socket;
        private CancellationTokenSource _cts;

        public ServerRegistry Registry => _registry;

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _socket = new UdpClient();
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress, true);
            _socket.Client.Bind(new IPEndPoint(IPAddress.Any, SharingService.DiscoveryPort));
            ReceiveLoop();
        }

        private async void ReceiveLoop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try { result = await _socket.ReceiveAsync(); }
                catch { break; }

                var json = Encoding.UTF8.GetString(result.Buffer);
                var beacon = DiscoveryBeacon.FromJson(json);
                if (beacon != null)
                    _registry.Observe(beacon, result.RemoteEndPoint.Address.ToString(),
                        DateTime.UtcNow);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _socket?.Close();
        }
    }
}
