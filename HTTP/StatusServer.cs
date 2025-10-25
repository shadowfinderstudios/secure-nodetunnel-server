using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using NodeTunnel.TCP;

namespace NodeTunnel.HTTP;

public class LobbyMetadata {
    public string LobbyId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Map { get; set; } = "";
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public bool IsPassworded { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> CustomFields { get; set; } = new();
}

public class StatusServer(SecureTCPHandler tcp) {
    private readonly HttpListener _http = new();
    private readonly CancellationTokenSource _ct = new();
    private readonly Dictionary<string, LobbyMetadata> _lobbies = new();
    public event Action<string>? RoomClosed;
    
    public async Task StartAsync() {
	tcp.RoomClosed += HandleRoomClosed;
        _http.Prefixes.Add("http://*:8099/");
        _http.Start();

        while (!_ct.Token.IsCancellationRequested) {
            try {
                var ctx = await _http.GetContextAsync();
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch (ObjectDisposedException) {
                break;
            }
        }
    }

    private void HandleRoomClosed(string roomId) {
	Console.WriteLine($"[StatusServer] Room closed: {roomId} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        lock (_lobbies) {
            _lobbies.Remove(roomId);
        }
	RoomClosed?.Invoke(roomId);
    }

    private async Task HandleRequest(HttpListenerContext ctx) {
        var req = ctx.Request;
        var res = ctx.Response;

        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        res.ContentType = "application/json";

        // Handle CORS preflight
        if (req.HttpMethod == "OPTIONS") {
            res.StatusCode = 200;
            res.OutputStream.Close();
            return;
        }

        try {
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;

            object? responseData = null;

            // Route requests
            if (path == "/register-lobby" && method == "POST") {
                responseData = await HandleRegisterLobby(req);
            }
            else if (path == "/lobbies" && method == "GET") {
                responseData = HandleGetLobbies();
            }
            else if (path == "/" || path == "/status") {
                responseData = GetServerStats();
            }
            else {
                res.StatusCode = 404;
                responseData = new { error = "Endpoint not found" };
            }

            var json = JsonSerializer.Serialize(responseData);
            var buffer = Encoding.UTF8.GetBytes(json);

            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex) {
            res.StatusCode = 500;
            var err = Encoding.UTF8.GetBytes($"{{\"error\": \"{ex.Message}\"}}");
            await res.OutputStream.WriteAsync(err, 0, err.Length);
        }
        finally {
            res.OutputStream.Close();
        }
    }

    private object GetServerStats() {
        var process = Process.GetCurrentProcess();
        var memUsage = process.WorkingSet64 / (1024 * 1024);
        var cpuUsage = GetCpuUsage();
	var roomDetails = tcp.GetRoomDetails();
        
        return new {
            status = "online",
            timestamp = DateTime.UtcNow,
            totalRooms = tcp.GetTotalRooms(),
            totalPeers = tcp.GetTotalPeers(),
	    rooms = roomDetails.Select(r => new {
                roomId = r.roomId,
		peerCount = r.peerCount
            }).ToList(),
            memoryUsageMB = memUsage,
            cpuUsagePercent = Math.Round(cpuUsage, 1)
        };
    }

    private double GetCpuUsage() {
        var process = Process.GetCurrentProcess();

        var startTime = DateTime.UtcNow;
        var startCpuUsage = process.TotalProcessorTime;

        Thread.Sleep(100);

        var endTime = DateTime.UtcNow;
        var endCpuUsage = process.TotalProcessorTime;

        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMsPassed = (endTime - startTime).TotalMilliseconds;
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

        return cpuUsageTotal * 100;
    }

    private async Task<object> HandleRegisterLobby(HttpListenerRequest req) {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        // Parse as dictionary for schema validation
        var lobbyDict = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
        if (lobbyDict == null) {
            throw new Exception("Invalid lobby data");
        }

        // Validate against schema
        var validation = LobbySchema.Validate(lobbyDict);
        if (!validation.IsValid) {
            throw new Exception($"Schema validation failed: {validation.ErrorMessage}");
        }

        // Helper to extract value from JsonElement or object
        string GetString(object obj) {
            if (obj is JsonElement element) {
                return element.GetString() ?? "";
            }
            return obj?.ToString() ?? "";
        }

        int GetInt(object obj) {
            if (obj is JsonElement element) {
                return element.GetInt32();
            }
            return Convert.ToInt32(obj);
        }

        bool GetBool(object obj) {
            if (obj is JsonElement element) {
                return element.GetBoolean();
            }
            return Convert.ToBoolean(obj);
        }

        // Convert to LobbyMetadata for storage
        var lobby = new LobbyMetadata {
            LobbyId = GetString(lobbyDict["LobbyId"]),
            Name = GetString(lobbyDict["Name"]),
            Mode = GetString(lobbyDict["Mode"]),
            Map = GetString(lobbyDict["Map"]),
            CurrentPlayers = GetInt(lobbyDict["CurrentPlayers"]),
            MaxPlayers = GetInt(lobbyDict["MaxPlayers"]),
            IsPassworded = GetBool(lobbyDict["IsPassworded"]),
            LastUpdated = DateTime.UtcNow,
            CustomFields = new Dictionary<string, object>()
        };

        // Store any custom fields
        var knownFields = new HashSet<string> { "LobbyId", "Name", "Mode", "Map", "CurrentPlayers", "MaxPlayers", "IsPassworded" };
        foreach (var kvp in lobbyDict) {
            if (!knownFields.Contains(kvp.Key)) {
                lobby.CustomFields[kvp.Key] = kvp.Value;
            }
        }

        // Store or update lobby
        lock (_lobbies) {
            _lobbies[lobby.LobbyId] = lobby;
        }

        return new {
            success = true,
            message = "Lobby registered successfully",
            lobbyId = lobby.LobbyId
        };
    }

    private object HandleGetLobbies() {
        lock (_lobbies) {
            var actualRooms = tcp.GetRoomDetails().ToDictionary(r => r.roomId, r => r.peerCount);

            // Clean up stale lobbies (older than 2 minute)
            var staleTime = DateTime.UtcNow.AddMinutes(-2);
            var staleLobbies = _lobbies
                .Where(kvp => kvp.Value.LastUpdated < staleTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var lobbyId in staleLobbies) {
                _lobbies.Remove(lobbyId);
            }

	    var invalidLobbies = _lobbies
                .Where(kvp => !actualRooms.ContainsKey(kvp.Key))
		.Select(kvp => kvp.Key)
		.ToList();

	    foreach (var lobbyId in invalidLobbies) {
                _lobbies.Remove(lobbyId);
	    }

            var lobbiesWithActualCounts = _lobbies.Values
                .Select(lobby => {
                    var lobbyData = new {
                        LobbyId = lobby.LobbyId,
                        Name = lobby.Name,
                        Mode = lobby.Mode,
                        Map = lobby.Map,
                        CurrentPlayers = actualRooms.GetValueOrDefault(lobby.LobbyId, 0),
                        MaxPlayers = lobby.MaxPlayers,
                        IsPassworded = lobby.IsPassworded,
                        LastUpdated = lobby.LastUpdated,
                        CustomFields = lobby.CustomFields
                    };
                    return lobbyData;
                })
                .OrderByDescending(l => l.LastUpdated)
                .ToList();

            return new {
                lobbies = lobbiesWithActualCounts,
                count = lobbiesWithActualCounts.Count
            };
        }
    }
}
