using NodeTunnel.HTTP;
using NodeTunnel.TCP;
using NodeTunnel.UDP;
using NodeTunnel.Security;

namespace NodeTunnel;

public class NodeTunnel {
    public static async Task Main() {
        Console.WriteLine("NodeTunnel Server - Secure Edition");
        Console.WriteLine();
        
        var securityConfig = new SecurityConfig
        {
            EnableEncryption = true,
            EnableAuthentication = true,
            EnableRateLimiting = true,
            MaxConnectionsPerIP = 10,
            MaxPacketsPerSecond = 20000,
            MaxPacketSize = 65536,
            BanDuration = TimeSpan.FromMinutes(30),
            
            MasterKey = Environment.GetEnvironmentVariable("NODETUNNEL_MASTER_KEY") 
                        ?? SecurityConfig.GenerateMasterKey()
        };
        
        var security = new SecurityLayer(securityConfig);
        
        if (Environment.GetEnvironmentVariable("NODETUNNEL_MASTER_KEY") == null)
        {
            Console.WriteLine();
            Console.WriteLine("No NODETUNNEL_MASTER_KEY environment variable found.");
            Console.WriteLine("A new key has been generated. Save it for your clients!");
            Console.WriteLine();
            
            await File.WriteAllTextAsync("master_key.txt", securityConfig.MasterKey);
            Console.WriteLine("Master key saved to: master_key.txt");
            Console.WriteLine();
        }
        
        var tcpHandler = new SecureTCPHandler(security);
        var udpHandler = new SecureUDPHandler(tcpHandler, security);
        var statusServer = new StatusServer(tcpHandler);

        tcpHandler.PeerDisconnected += udpHandler.HandlePeerDisconnected;
        
        try {
            var tcpTask = tcpHandler.StartTcpAsync();
            Console.WriteLine("TCP Started");
            
            var udpTask = udpHandler.StartUdpAsync();
            Console.WriteLine("UDP Started");
            
            var statusTask = statusServer.StartAsync();
            Console.WriteLine("HTTP Started");
            
            Console.WriteLine();
            Console.WriteLine("Server running securely");
            Console.WriteLine("Press Ctrl+C to stop");
            Console.WriteLine();

            var completedTask = await Task.WhenAny(tcpTask, udpTask, statusTask);
            if (completedTask == tcpTask) {
                Console.WriteLine("TCP task completed");
                if (tcpTask.IsFaulted) Console.WriteLine($"TCP error: {tcpTask.Exception?.GetBaseException().Message}");
            }
            else if (completedTask == udpTask) {
                Console.WriteLine("UDP task completed");
                if (udpTask.IsFaulted) Console.WriteLine($"UDP error: {udpTask.Exception?.GetBaseException().Message}");
            }
            else if (completedTask == statusTask) {
                Console.WriteLine("HTTP task completed");
                if (statusTask.IsFaulted) Console.WriteLine($"HTTP error: {statusTask.Exception?.GetBaseException().Message}");
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Server error: {ex.Message}");
        }
        
        Console.WriteLine("Server stopped.");
    }
}

