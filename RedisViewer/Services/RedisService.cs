using System.Runtime.CompilerServices;
using StackExchange.Redis;

namespace RedisViewer.Services;

public class RedisService : IDisposable
{
    private ConnectionMultiplexer? _connection;
    private string? _currentConnectionString;

    public bool IsConnected => _connection?.IsConnected ?? false;
    public string? CurrentHost { get; private set; }
    public int? CurrentPort { get; private set; }

    public async Task<bool> ConnectAsync(string host, int port, string? password = null)
    {
        // Disconnect existing connection if any
        Disconnect();

        try
        {
            var configOptions = new ConfigurationOptions
            {
                EndPoints = { { host, port } },
                AbortOnConnectFail = false,
                ConnectTimeout = 5000,
                SyncTimeout = 5000,
                AllowAdmin = true
            };

            if (!string.IsNullOrWhiteSpace(password))
            {
                configOptions.Password = password;
            }

            _connection = await ConnectionMultiplexer.ConnectAsync(configOptions);

            if (_connection.IsConnected)
            {
                CurrentHost = host;
                CurrentPort = port;
                _currentConnectionString = $"{host}:{port}";
            }

            return _connection.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    public void Disconnect()
    {
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
        CurrentHost = null;
        CurrentPort = null;
        _currentConnectionString = null;
    }

    private IDatabase GetDatabase() => _connection?.GetDatabase()
        ?? throw new InvalidOperationException("Not connected to Redis");

    private IServer GetServer()
    {
        if (_connection == null)
            throw new InvalidOperationException("Not connected to Redis");

        var endpoint = _connection.GetEndPoints().First();
        return _connection.GetServer(endpoint);
    }

    public async IAsyncEnumerable<RedisKeyInfo> StreamKeysAsync(
        string pattern = "*",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var server = GetServer();
        var db = GetDatabase();

        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var type = await db.KeyTypeAsync(key);
            var ttl = await db.KeyTimeToLiveAsync(key);
            var size = await GetKeySizeAsync(db, key, type);

            yield return new RedisKeyInfo
            {
                Key = key.ToString(),
                Type = type.ToString(),
                Ttl = ttl,
                Size = size
            };
        }
    }

    private async Task<long> GetKeySizeAsync(IDatabase db, RedisKey key, RedisType type)
    {
        try
        {
            return type switch
            {
                RedisType.String => (await db.StringGetAsync(key)).Length(),
                RedisType.List => await db.ListLengthAsync(key),
                RedisType.Set => await db.SetLengthAsync(key),
                RedisType.SortedSet => await db.SortedSetLengthAsync(key),
                RedisType.Hash => await db.HashLengthAsync(key),
                RedisType.Stream => await db.StreamLengthAsync(key),
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    public async Task<RedisValueResult> GetValueAsync(string key)
    {
        var db = GetDatabase();
        var type = await db.KeyTypeAsync(key);

        return type switch
        {
            RedisType.String => new RedisValueResult
            {
                Type = "String",
                Value = await db.StringGetAsync(key)
            },
            RedisType.List => new RedisValueResult
            {
                Type = "List",
                ListValue = (await db.ListRangeAsync(key)).Select(v => v.ToString()).ToList()
            },
            RedisType.Set => new RedisValueResult
            {
                Type = "Set",
                SetValue = (await db.SetMembersAsync(key)).Select(v => v.ToString()).ToList()
            },
            RedisType.SortedSet => new RedisValueResult
            {
                Type = "SortedSet",
                SortedSetValue = (await db.SortedSetRangeByRankWithScoresAsync(key))
                    .Select(v => new SortedSetEntry(v.Element.ToString(), v.Score))
                    .ToList()
            },
            RedisType.Hash => new RedisValueResult
            {
                Type = "Hash",
                HashValue = (await db.HashGetAllAsync(key))
                    .ToDictionary(h => h.Name.ToString(), h => h.Value.ToString())
            },
            RedisType.Stream => new RedisValueResult
            {
                Type = "Stream",
                Value = "[Stream data - viewing not implemented]"
            },
            _ => new RedisValueResult
            {
                Type = type.ToString(),
                Value = "[Unknown type]"
            }
        };
    }

    public async Task<bool> DeleteKeyAsync(string key)
    {
        var db = GetDatabase();
        return await db.KeyDeleteAsync(key);
    }

    public async Task<bool> SetStringAsync(string key, string value)
    {
        var db = GetDatabase();
        return await db.StringSetAsync(key, value);
    }

    public async Task<bool> SetHashFieldAsync(string key, string field, string value)
    {
        var db = GetDatabase();
        return await db.HashSetAsync(key, field, value);
    }

    public async Task<long> GetDbSizeAsync()
    {
        var server = GetServer();
        return await server.DatabaseSizeAsync();
    }

    public async Task<ServerInfo> GetServerInfoAsync()
    {
        var server = GetServer();
        var info = await server.InfoAsync();

        var serverSection = info.FirstOrDefault(g => g.Key == "Server");
        var memorySection = info.FirstOrDefault(g => g.Key == "Memory");
        var clientsSection = info.FirstOrDefault(g => g.Key == "Clients");

        return new ServerInfo
        {
            RedisVersion = serverSection?.FirstOrDefault(x => x.Key == "redis_version").Value ?? "Unknown",
            UsedMemory = memorySection?.FirstOrDefault(x => x.Key == "used_memory_human").Value ?? "Unknown",
            ConnectedClients = int.TryParse(clientsSection?.FirstOrDefault(x => x.Key == "connected_clients").Value, out var clients) ? clients : 0,
            UptimeInSeconds = long.TryParse(serverSection?.FirstOrDefault(x => x.Key == "uptime_in_seconds").Value, out var uptime) ? uptime : 0
        };
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}

public class RedisKeyInfo
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public TimeSpan? Ttl { get; set; }
    public long Size { get; set; }
}

public class RedisValueResult
{
    public string Type { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<string>? ListValue { get; set; }
    public List<string>? SetValue { get; set; }
    public List<SortedSetEntry>? SortedSetValue { get; set; }
    public Dictionary<string, string>? HashValue { get; set; }
}

public class SortedSetEntry
{
    public string Member { get; set; }
    public double Score { get; set; }

    public SortedSetEntry(string member, double score)
    {
        Member = member;
        Score = score;
    }
}

public class ServerInfo
{
    public string RedisVersion { get; set; } = string.Empty;
    public string UsedMemory { get; set; } = string.Empty;
    public int ConnectedClients { get; set; }
    public long UptimeInSeconds { get; set; }

    public string FormattedUptime
    {
        get
        {
            var ts = TimeSpan.FromSeconds(UptimeInSeconds);
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h";
            return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }
    }
}
