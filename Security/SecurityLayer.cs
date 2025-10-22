using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;

namespace NodeTunnel.Security;

public class SecurityConfig
{
    public bool EnableEncryption { get; set; } = true;
    public bool EnableAuthentication { get; set; } = true;
    public bool EnableRateLimiting { get; set; } = true;
    public int MaxConnectionsPerIP { get; set; } = 10;
    public int MaxPacketsPerSecond { get; set; } = 3000;
    public int MaxPacketSize { get; set; } = 65536;
    public TimeSpan BanDuration { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan TokenExpiration { get; set; } = TimeSpan.FromHours(24);
    
    public string MasterKey { get; set; } = GenerateMasterKey();
    
    public static string GenerateMasterKey()
    {
        var key = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(key);
        }
        return Convert.ToBase64String(key);
    }
}

public class PacketEncryption
{
    private readonly byte[] _key;
    
    public PacketEncryption(string masterKey)
    {
        _key = Convert.FromBase64String(masterKey);
        if (_key.Length != 32)
            throw new ArgumentException("Master key must be 32 bytes (256 bits) for AES-256");
    }
    
    public byte[] Encrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length);
        
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        
        return result;
    }
    
    public byte[]? Decrypt(byte[] data)
    {
        try
        {
            if (data.Length < 16) return null;
            
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            var iv = new byte[16];
            Buffer.BlockCopy(data, 0, iv, 0, 16);
            aes.IV = iv;
            
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 16, data.Length - 16);
        }
        catch
        {
            return null;
        }
    }
}

public class AuthToken
{
    public string Token { get; set; } = string.Empty;
    public string OnlineId { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
    public IPAddress IpAddress { get; set; } = null!;
    
    public bool IsValid() => DateTime.UtcNow < Expiration;
}

public class AuthManager
{
    private readonly ConcurrentDictionary<string, AuthToken> _tokens = new();
    private readonly SecurityConfig _config;
    
    public AuthManager(SecurityConfig config)
    {
        _config = config;
        
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                CleanupExpiredTokens();
            }
        });
    }
    
    public string GenerateToken(string onlineId, IPAddress ipAddress)
    {
        var tokenBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        
        var token = Convert.ToBase64String(tokenBytes);
        var authToken = new AuthToken
        {
            Token = token,
            OnlineId = onlineId,
            Expiration = DateTime.UtcNow.Add(_config.TokenExpiration),
            IpAddress = ipAddress
        };
        
        _tokens[token] = authToken;
        Console.WriteLine($"[AUTH] Generated token for {onlineId} from {ipAddress}");
        return token;
    }
    
    public bool ValidateToken(string token, IPAddress ipAddress)
    {
        if (!_tokens.TryGetValue(token, out var authToken))
            return false;
        
        if (!authToken.IsValid())
        {
            _tokens.TryRemove(token, out _);
            return false;
        }
        
        return true;
    }
    
    public void RevokeToken(string token)
    {
        _tokens.TryRemove(token, out _);
    }
    
    private void CleanupExpiredTokens()
    {
        var expired = _tokens.Where(kvp => !kvp.Value.IsValid()).Select(kvp => kvp.Key).ToList();
        foreach (var token in expired)
        {
            _tokens.TryRemove(token, out _);
        }
        
        if (expired.Count > 0)
            Console.WriteLine($"[AUTH] Cleaned up {expired.Count} expired tokens");
    }
}

public class RateLimiter
{
    private class ClientStats
    {
        public int PacketCount { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime? BannedUntil { get; set; }
        public int ViolationCount { get; set; }
        public int TotalPackets { get; set; }
        public DateTime FirstSeen { get; set; }
        public int PeakRate { get; set; }
    }
    
    private readonly ConcurrentDictionary<IPAddress, ClientStats> _stats = new();
    private readonly SecurityConfig _config;
    
    public RateLimiter(SecurityConfig config)
    {
        _config = config;
        
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                CleanupOldEntries();
            }
        });
    }
    
    public bool AllowPacket(IPAddress ipAddress)
    {
        var now = DateTime.UtcNow;
        var stats = _stats.GetOrAdd(ipAddress, _ => new ClientStats
        {
            WindowStart = now,
            PacketCount = 0,
            FirstSeen = now,
            TotalPackets = 0,
            PeakRate = 0
        });
        
        if (stats.BannedUntil.HasValue)
        {
            if (now < stats.BannedUntil.Value)
                return false;
            
            Console.WriteLine($"[RATE LIMIT] Unbanned {ipAddress} after serving ban duration");
            stats.BannedUntil = null;
            stats.PacketCount = 0;
            stats.WindowStart = now;
            stats.ViolationCount = 0;
        }
        
        if ((now - stats.WindowStart).TotalSeconds >= 1)
        {
            stats.PacketCount = 0;
            stats.WindowStart = now;
        }
        
        stats.PacketCount++;
        stats.TotalPackets++;
        
        if (stats.PacketCount > stats.PeakRate)
            stats.PeakRate = stats.PacketCount;
        
        if (stats.PacketCount > _config.MaxPacketsPerSecond)
        {
            stats.ViolationCount++;
            
            var currentRate = stats.PacketCount;
            var timeConnected = (now - stats.FirstSeen).TotalSeconds;
            var avgRate = timeConnected > 0 ? stats.TotalPackets / timeConnected : 0;
            
            var banMultiplier = Math.Min(stats.ViolationCount, 10);
            var banDuration = TimeSpan.FromMinutes(_config.BanDuration.TotalMinutes * banMultiplier);
            stats.BannedUntil = now.Add(banDuration);
            
            Console.WriteLine($"╔══════════════════════════════════════════════════════════");
            Console.WriteLine($"║ [RATE LIMIT] IP BANNED");
            Console.WriteLine($"╠══════════════════════════════════════════════════════════");
            Console.WriteLine($"║ IP Address:      {ipAddress}");
            Console.WriteLine($"║ Ban Duration:    {banDuration.TotalMinutes:F1} minutes");
            Console.WriteLine($"║ Violation #:     {stats.ViolationCount}");
            Console.WriteLine($"╠══════════════════════════════════════════════════════════");
            Console.WriteLine($"║ STATISTICS:");
            Console.WriteLine($"║ Current Rate:    {currentRate} packets/sec (limit: {_config.MaxPacketsPerSecond})");
            Console.WriteLine($"║ Peak Rate:       {stats.PeakRate} packets/sec");
            Console.WriteLine($"║ Average Rate:    {avgRate:F1} packets/sec");
            Console.WriteLine($"║ Total Packets:   {stats.TotalPackets}");
            Console.WriteLine($"║ Time Connected:  {timeConnected:F1} seconds");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════");
            
            return false;
        }
        
        return true;
    }
    
    public bool IsBanned(IPAddress ipAddress)
    {
        if (!_stats.TryGetValue(ipAddress, out var stats))
            return false;
        
        if (stats.BannedUntil.HasValue && DateTime.UtcNow < stats.BannedUntil.Value)
            return true;
        
        return false;
    }
    
    private void CleanupOldEntries()
    {
        var now = DateTime.UtcNow;
        var toRemove = _stats
            .Where(kvp => kvp.Value.BannedUntil == null && 
                          (now - kvp.Value.WindowStart).TotalMinutes > 5)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var ip in toRemove)
        {
            _stats.TryRemove(ip, out _);
        }
    }
}

public class ConnectionManager
{
    private readonly ConcurrentDictionary<IPAddress, int> _connections = new();
    private readonly SecurityConfig _config;
    
    public ConnectionManager(SecurityConfig config)
    {
        _config = config;
    }
    
    public bool AllowConnection(IPAddress ipAddress)
    {
        var count = _connections.GetOrAdd(ipAddress, 0);
        
        if (count >= _config.MaxConnectionsPerIP)
        {
            Console.WriteLine($"[CONNECTION] Rejected {ipAddress}: max connections ({count}/{_config.MaxConnectionsPerIP})");
            return false;
        }
        
        _connections[ipAddress] = count + 1;
        return true;
    }
    
    public void RemoveConnection(IPAddress ipAddress)
    {
        if (_connections.TryGetValue(ipAddress, out var count))
        {
            if (count <= 1)
                _connections.TryRemove(ipAddress, out _);
            else
                _connections[ipAddress] = count - 1;
        }
    }
    
    public int GetConnectionCount(IPAddress ipAddress)
    {
        return _connections.GetOrAdd(ipAddress, 0);
    }
}

public class PacketValidator
{
    private readonly SecurityConfig _config;
    
    public PacketValidator(SecurityConfig config)
    {
        _config = config;
    }
    
    public bool ValidatePacket(byte[] data, IPAddress source)
    {
        if (data == null || data.Length == 0)
        {
            Console.WriteLine($"[VALIDATION] Empty packet from {source}");
            return false;
        }
        
        if (data.Length > _config.MaxPacketSize)
        {
            Console.WriteLine($"[VALIDATION] Oversized packet from {source}: {data.Length} bytes");
            return false;
        }
        
        if (data.Length < 4)
        {
            Console.WriteLine($"[VALIDATION] Undersized packet from {source}");
            return false;
        }
        
        return true;
    }
    
    public bool ValidateOnlineId(string oid)
    {
        if (string.IsNullOrEmpty(oid))
            return false;
        
        if (oid.Length != 8)
            return false;
        
        return oid.All(c => char.IsLetterOrDigit(c) && char.IsUpper(c) || char.IsDigit(c));
    }
}

public class SecurityLayer
{
    public SecurityConfig Config { get; }
    public PacketEncryption Encryption { get; }
    public AuthManager Auth { get; }
    public RateLimiter RateLimiter { get; }
    public ConnectionManager Connections { get; }
    public PacketValidator Validator { get; }
    
    public SecurityLayer(SecurityConfig? config = null)
    {
        Config = config ?? new SecurityConfig();
        Encryption = new PacketEncryption(Config.MasterKey);
        Auth = new AuthManager(Config);
        RateLimiter = new RateLimiter(Config);
        Connections = new ConnectionManager(Config);
        Validator = new PacketValidator(Config);
        
        PrintSecurityStatus();
    }
    
    private void PrintSecurityStatus()
    {
        Console.WriteLine("NODETUNNEL SECURITY LAYER INITIALIZED");
        Console.WriteLine($"Encryption:     {(Config.EnableEncryption ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"Authentication: {(Config.EnableAuthentication ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"Rate Limiting:  {(Config.EnableRateLimiting ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"Max Connections/IP: {Config.MaxConnectionsPerIP}");
        Console.WriteLine($"Max Packets/Sec:    {Config.MaxPacketsPerSecond}");
        Console.WriteLine($"Max Packet Size:    {Config.MaxPacketSize} bytes");
        
        if (Config.EnableEncryption)
        {
            Console.WriteLine($"Master Encryption Key: {Config.MasterKey}");
            Console.WriteLine("SAVE THIS KEY SECURELY - Clients will need it!");
            Console.WriteLine("Set NODETUNNEL_MASTER_KEY environment variable in production");
        }
    }
    
    public byte[]? ProcessIncomingPacket(byte[] data, IPAddress source)
    {
        if (Config.EnableRateLimiting && !RateLimiter.AllowPacket(source))
            return null;
        
        if (!Validator.ValidatePacket(data, source))
            return null;
        
        if (Config.EnableEncryption)
        {
            var decrypted = Encryption.Decrypt(data);
            if (decrypted == null)
            {
                Console.WriteLine($"[SECURITY] Failed to decrypt packet from {source}");
                return null;
            }
            return decrypted;
        }
        
        return data;
    }
    
    public byte[] ProcessOutgoingPacket(byte[] data)
    {
        if (Config.EnableEncryption)
        {
            return Encryption.Encrypt(data);
        }
        return data;
    }
    
    public bool IsIPBanned(IPAddress ipAddress)
    {
        if (!Config.EnableRateLimiting)
            return false;
        
        return RateLimiter.IsBanned(ipAddress);
    }
}
