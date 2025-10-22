using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NodeTunnel.TCP;
using NodeTunnel.Utils;
using NodeTunnel.Security;

namespace NodeTunnel.UDP;

public class SecureUDPHandler {
    private UdpClient _udp = null!;
    private SecureTCPHandler _tcp = null!;
    private CancellationTokenSource _ct = null!;
    private ConcurrentDictionary<string, IPEndPoint> _oidToEndpoint = new();
    
    private readonly SecurityLayer _security;

    public SecureUDPHandler(SecureTCPHandler tcpHandler, SecurityLayer security) {
        _tcp = tcpHandler;
        _security = security;
    }
    
    public async Task StartUdpAsync(string host = "0.0.0.0", int port = 9999) {
        _udp = new UdpClient(port);
        _ct = new CancellationTokenSource();
        
        Console.WriteLine($"UDP Listening on {host}:{port}");

        await HandleUdpPackets();
    }

    public async Task HandleUdpPackets() {
        try {
            while (!_ct.Token.IsCancellationRequested) {
                var res = await _udp.ReceiveAsync();
                var data = res.Buffer;
                var endpoint = res.RemoteEndPoint;
                var clientIp = endpoint.Address;

                if (_security.IsIPBanned(clientIp)) {
                    continue;
                }

                var processedData = _security.ProcessIncomingPacket(data, clientIp);
                if (processedData == null) {
                    Console.WriteLine($"[SECURITY] Rejected UDP packet from {clientIp}");
                    continue;
                }

                await ProcessUdpPacket(processedData, endpoint);
            }
        }
        catch (ObjectDisposedException) {
            Console.WriteLine("UDP Handler shutting down");
        }
        catch (Exception ex) {
            Console.WriteLine($"UDP Handler Error: {ex.Message}");
        }
    }

    private async Task ProcessUdpPacket(byte[] data, IPEndPoint endpoint) {
        if (data.Length < 8) return;

        var senderOidLen = ByteUtils.UnpackU32(data, 0);
        if (data.Length < senderOidLen + 8) return;

        var senderOid = Encoding.UTF8.GetString(data, 4, (int)senderOidLen);
        
        if (!_security.Validator.ValidateOnlineId(senderOid)) {
            Console.WriteLine($"[SECURITY] Invalid sender OID in UDP packet");
            return;
        }
        
        _oidToEndpoint[senderOid] = endpoint;

        var targetOidLen = ByteUtils.UnpackU32(data, 4 + (int)senderOidLen);
        if (data.Length < 8 + senderOidLen + targetOidLen) return;

        var targetOid = Encoding.UTF8.GetString(data, 8 + (int)senderOidLen, (int)targetOidLen);
        var gameData = data.Skip(8 + (int)senderOidLen + (int)targetOidLen).ToArray();

        if (targetOid == "SERVER") {
            var msg = Encoding.UTF8.GetString(gameData);

            if (msg == "UDP_CONNECT") {
                var response = CreateRelayPacket("SERVER", senderOid, Encoding.UTF8.GetBytes("UDP_CONNECT_RES"));
                
                var encryptedResponse = _security.ProcessOutgoingPacket(response);
                await _udp.SendAsync(encryptedResponse, endpoint);
            }
            return;
        }
        
        if (targetOid != "0" && !_security.Validator.ValidateOnlineId(targetOid)) {
            Console.WriteLine($"[SECURITY] Invalid target OID in UDP packet");
            return;
        }
        
        var room = _tcp.GetRoomForPeer(senderOid);
        if (room == null) return;

        try {
            if (targetOid == "0") {
                foreach (var peer in room.GetPeers()) {
                    if (peer.oid != senderOid && _oidToEndpoint.ContainsKey(peer.oid)) {
                        Console.WriteLine($"Broadcasting to {peer.oid} at {_oidToEndpoint[peer.oid]}");
                        
                        var relayPacket = CreateRelayPacket(senderOid, peer.oid, gameData);
                        
                        var encryptedPacket = _security.ProcessOutgoingPacket(relayPacket);
                        await _udp.SendAsync(encryptedPacket, _oidToEndpoint[peer.oid]);
                    }
                }
            }
            else {
                if (room.HasPeer(targetOid) && _oidToEndpoint.ContainsKey(targetOid)) {
                    var relayPacket = CreateRelayPacket(senderOid, targetOid, gameData);
                    
                    var encryptedPacket = _security.ProcessOutgoingPacket(relayPacket);
                    await _udp.SendAsync(encryptedPacket, _oidToEndpoint[targetOid]);
                }
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Error relaying UDP packet: {ex.Message}");
        }
    }

    private byte[] CreateRelayPacket(string fromOid, string toOid, byte[] gameData) {
        var packet = new List<byte>();
        
        packet.AddRange(ByteUtils.PackU32((uint)fromOid.Length));
        packet.AddRange(Encoding.UTF8.GetBytes(fromOid));
        
        packet.AddRange(ByteUtils.PackU32((uint)toOid.Length));
        packet.AddRange(Encoding.UTF8.GetBytes(toOid));
        
        packet.AddRange(gameData);
        
        return packet.ToArray();
    }

    public void HandlePeerDisconnected(string oid) {
        _oidToEndpoint.TryRemove(oid, out _);
        Console.WriteLine($"UDP: Removed peer {oid}");
    }

    public void HandlePeersDisconnected(IEnumerable<string> oids) {
        foreach (var oid in oids) {
            HandlePeerDisconnected(oid);
        }
    }
}

