using System.Runtime.CompilerServices;
using StackExchange.Redis;

namespace RedisViewer.Services;

public class RedisService : IDisposable
{
    private ConnectionMultiplexer? _connection;
    private string? _currentConnectionString;
    private int _selectedDatabase;

    public bool IsConnected => _connection?.IsConnected ?? false;
    public string? CurrentHost { get; private set; }
    public int? CurrentPort { get; private set; }
    public int SelectedDatabase => _selectedDatabase;
    public int DatabaseCount => 16; // Redis default

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

    private IDatabase GetDatabase() => _connection?.GetDatabase(_selectedDatabase)
        ?? throw new InvalidOperationException("Not connected to Redis");

    private IServer GetServer()
    {
        if (_connection == null)
            throw new InvalidOperationException("Not connected to Redis");

        var endpoint = _connection.GetEndPoints().First();
        return _connection.GetServer(endpoint);
    }

    public void SelectDatabase(int dbIndex)
    {
        if (dbIndex < 0 || dbIndex >= DatabaseCount)
            throw new ArgumentOutOfRangeException(nameof(dbIndex));
        _selectedDatabase = dbIndex;
    }

    // Execute raw Redis command
    public async Task<string> ExecuteCommandAsync(string command)
    {
        var db = GetDatabase();
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "Empty command";

        try
        {
            var result = await db.ExecuteAsync(parts[0], parts.Skip(1).Cast<object>().ToArray());
            return FormatRedisResult(result);
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private string FormatRedisResult(RedisResult result)
    {
        if (result.IsNull) return "(nil)";
        if (result.Resp3Type == ResultType.Array || result.Resp3Type == ResultType.Set)
        {
            var items = (RedisResult[])result!;
            if (items.Length == 0) return "(empty array)";
            return string.Join("\n", items.Select((item, i) => $"{i + 1}) {FormatRedisResult(item)}"));
        }
        return result.ToString() ?? "(nil)";
    }

    // Get slow log
    public async Task<List<SlowLogEntry>> GetSlowLogAsync(int count = 20)
    {
        var server = GetServer();
        var entries = await server.SlowlogGetAsync(count);
        return entries.Select(e => new SlowLogEntry
        {
            Id = e.UniqueId,
            Timestamp = e.Time,
            Duration = e.Duration,
            Command = string.Join(" ", e.Arguments?.Select(a => a.ToString()) ?? Array.Empty<string>())
        }).ToList();
    }

    // Get client list
    public async Task<List<RedisClientInfo>> GetClientListAsync()
    {
        var server = GetServer();
        var clients = await server.ClientListAsync();
        return clients.Select(c => new RedisClientInfo
        {
            Id = c.Id,
            Address = c.Address?.ToString() ?? "",
            Name = c.Name ?? "",
            AgeSeconds = (int)c.AgeSeconds,
            IdleSeconds = (int)c.IdleSeconds,
            Database = c.Database,
            Flags = c.FlagsRaw ?? ""
        }).ToList();
    }

    // Get key statistics by type
    public async Task<Dictionary<string, KeyTypeStats>> GetKeyStatsByTypeAsync(CancellationToken cancellationToken = default)
    {
        var server = GetServer();
        var db = GetDatabase();
        var stats = new Dictionary<string, KeyTypeStats>
        {
            ["String"] = new KeyTypeStats(),
            ["List"] = new KeyTypeStats(),
            ["Set"] = new KeyTypeStats(),
            ["SortedSet"] = new KeyTypeStats(),
            ["Hash"] = new KeyTypeStats(),
            ["Stream"] = new KeyTypeStats(),
            ["Other"] = new KeyTypeStats()
        };

        await foreach (var key in server.KeysAsync(database: _selectedDatabase, pattern: "*").WithCancellation(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            var type = await db.KeyTypeAsync(key);
            var typeName = type.ToString();
            if (!stats.ContainsKey(typeName)) typeName = "Other";

            stats[typeName].Count++;
        }

        return stats;
    }

    // Get memory usage for a key
    public async Task<long> GetKeyMemoryUsageAsync(string key)
    {
        var db = GetDatabase();
        try
        {
            var result = await db.ExecuteAsync("MEMORY", "USAGE", key);
            return result.IsNull ? 0 : (long)result;
        }
        catch
        {
            return 0;
        }
    }

    // Bulk set TTL on multiple keys
    public async Task<int> SetBulkTtlAsync(IEnumerable<string> keys, TimeSpan? expiry)
    {
        var db = GetDatabase();
        int count = 0;
        foreach (var key in keys)
        {
            bool success;
            if (expiry == null)
                success = await db.KeyPersistAsync(key);
            else
                success = await db.KeyExpireAsync(key, expiry);
            if (success) count++;
        }
        return count;
    }

    // Subscribe to a Pub/Sub channel
    public ISubscriber GetSubscriber()
    {
        if (_connection == null)
            throw new InvalidOperationException("Not connected to Redis");
        return _connection.GetSubscriber();
    }

    // Publish a message
    public async Task<long> PublishAsync(string channel, string message)
    {
        var subscriber = GetSubscriber();
        return await subscriber.PublishAsync(RedisChannel.Literal(channel), message);
    }

    // Get database info (size per DB)
    public async Task<Dictionary<int, long>> GetAllDatabaseSizesAsync()
    {
        var server = GetServer();
        var sizes = new Dictionary<int, long>();

        for (int i = 0; i < DatabaseCount; i++)
        {
            try
            {
                var size = await server.DatabaseSizeAsync(i);
                sizes[i] = size;
            }
            catch
            {
                sizes[i] = 0;
            }
        }
        return sizes;
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

    public async Task<bool> DeleteHashFieldAsync(string key, string field)
    {
        var db = GetDatabase();
        return await db.HashDeleteAsync(key, field);
    }

    // List operations
    public async Task<long> ListPushAsync(string key, string value, bool pushRight = true)
    {
        var db = GetDatabase();
        return pushRight
            ? await db.ListRightPushAsync(key, value)
            : await db.ListLeftPushAsync(key, value);
    }

    public async Task<long> ListRemoveAsync(string key, string value, long count = 0)
    {
        var db = GetDatabase();
        return await db.ListRemoveAsync(key, value, count);
    }

    public async Task ListSetAsync(string key, long index, string value)
    {
        var db = GetDatabase();
        await db.ListSetByIndexAsync(key, index, value);
    }

    // Set operations
    public async Task<bool> SetAddAsync(string key, string value)
    {
        var db = GetDatabase();
        return await db.SetAddAsync(key, value);
    }

    public async Task<bool> SetRemoveAsync(string key, string value)
    {
        var db = GetDatabase();
        return await db.SetRemoveAsync(key, value);
    }

    // Sorted Set operations
    public async Task<bool> SortedSetAddAsync(string key, string member, double score)
    {
        var db = GetDatabase();
        return await db.SortedSetAddAsync(key, member, score);
    }

    public async Task<bool> SortedSetRemoveAsync(string key, string member)
    {
        var db = GetDatabase();
        return await db.SortedSetRemoveAsync(key, member);
    }

    // Key existence check
    public async Task<bool> KeyExistsAsync(string key)
    {
        var db = GetDatabase();
        return await db.KeyExistsAsync(key);
    }

    // TTL Management
    public async Task<bool> SetKeyExpireAsync(string key, TimeSpan? expiry)
    {
        var db = GetDatabase();
        if (expiry == null)
            return await db.KeyPersistAsync(key); // Remove TTL
        return await db.KeyExpireAsync(key, expiry);
    }

    public async Task<TimeSpan?> GetKeyTtlAsync(string key)
    {
        var db = GetDatabase();
        return await db.KeyTimeToLiveAsync(key);
    }

    // Rename key
    public async Task<bool> RenameKeyAsync(string oldKey, string newKey)
    {
        var db = GetDatabase();
        return await db.KeyRenameAsync(oldKey, newKey);
    }

    // Duplicate key
    public async Task<bool> DuplicateKeyAsync(string sourceKey, string destKey)
    {
        var db = GetDatabase();
        var type = await db.KeyTypeAsync(sourceKey);
        var ttl = await db.KeyTimeToLiveAsync(sourceKey);

        switch (type)
        {
            case RedisType.String:
                var strVal = await db.StringGetAsync(sourceKey);
                await db.StringSetAsync(destKey, strVal);
                break;
            case RedisType.Hash:
                var hashEntries = await db.HashGetAllAsync(sourceKey);
                await db.HashSetAsync(destKey, hashEntries);
                break;
            case RedisType.List:
                var listValues = await db.ListRangeAsync(sourceKey);
                foreach (var val in listValues)
                    await db.ListRightPushAsync(destKey, val);
                break;
            case RedisType.Set:
                var setMembers = await db.SetMembersAsync(sourceKey);
                foreach (var member in setMembers)
                    await db.SetAddAsync(destKey, member);
                break;
            case RedisType.SortedSet:
                var sortedSetEntries = await db.SortedSetRangeByRankWithScoresAsync(sourceKey);
                foreach (var entry in sortedSetEntries)
                    await db.SortedSetAddAsync(destKey, entry.Element, entry.Score);
                break;
            default:
                return false;
        }

        if (ttl.HasValue)
            await db.KeyExpireAsync(destKey, ttl);

        return true;
    }

    // Export key to JSON
    public async Task<string> ExportKeyToJsonAsync(string key)
    {
        var value = await GetValueAsync(key);
        var ttl = await GetKeyTtlAsync(key);

        var export = new KeyExport
        {
            Key = key,
            Type = value.Type,
            Ttl = ttl?.TotalSeconds,
            Value = value.Type switch
            {
                "String" => value.Value,
                "List" => value.ListValue,
                "Set" => value.SetValue,
                "SortedSet" => value.SortedSetValue?.Select(e => new { e.Member, e.Score }).ToList(),
                "Hash" => value.HashValue,
                _ => null
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    // Import key from JSON
    public async Task<bool> ImportKeyFromJsonAsync(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var key = root.GetProperty("Key").GetString()!;
            var type = root.GetProperty("Type").GetString()!;
            var db = GetDatabase();

            // Delete existing key if exists
            await db.KeyDeleteAsync(key);

            switch (type)
            {
                case "String":
                    var strValue = root.GetProperty("Value").GetString();
                    await db.StringSetAsync(key, strValue);
                    break;
                case "List":
                    foreach (var item in root.GetProperty("Value").EnumerateArray())
                        await db.ListRightPushAsync(key, item.GetString());
                    break;
                case "Set":
                    foreach (var item in root.GetProperty("Value").EnumerateArray())
                        await db.SetAddAsync(key, item.GetString());
                    break;
                case "SortedSet":
                    foreach (var item in root.GetProperty("Value").EnumerateArray())
                    {
                        var member = item.GetProperty("Member").GetString();
                        var score = item.GetProperty("Score").GetDouble();
                        await db.SortedSetAddAsync(key, member, score);
                    }
                    break;
                case "Hash":
                    foreach (var prop in root.GetProperty("Value").EnumerateObject())
                        await db.HashSetAsync(key, prop.Name, prop.Value.GetString());
                    break;
                default:
                    return false;
            }

            // Set TTL if present
            if (root.TryGetProperty("Ttl", out var ttlElement) && ttlElement.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                var ttlSeconds = ttlElement.GetDouble();
                await db.KeyExpireAsync(key, TimeSpan.FromSeconds(ttlSeconds));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // Delete multiple keys
    public async Task<long> DeleteKeysAsync(IEnumerable<string> keys)
    {
        var db = GetDatabase();
        var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
        return await db.KeyDeleteAsync(redisKeys);
    }

    // Search in values (for strings only, returns matching keys)
    public async IAsyncEnumerable<RedisKeyInfo> SearchInValuesAsync(
        string searchText,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var server = GetServer();
        var db = GetDatabase();

        await foreach (var key in server.KeysAsync(pattern: "*").WithCancellation(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var type = await db.KeyTypeAsync(key);
            bool matches = false;

            try
            {
                switch (type)
                {
                    case RedisType.String:
                        var strVal = await db.StringGetAsync(key);
                        matches = strVal.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase);
                        break;
                    case RedisType.Hash:
                        var hashEntries = await db.HashGetAllAsync(key);
                        matches = hashEntries.Any(h =>
                            h.Name.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                            h.Value.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase));
                        break;
                    case RedisType.List:
                        var listValues = await db.ListRangeAsync(key, 0, 100); // Limit for performance
                        matches = listValues.Any(v => v.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase));
                        break;
                    case RedisType.Set:
                        var setMembers = await db.SetMembersAsync(key);
                        matches = setMembers.Any(m => m.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase));
                        break;
                    case RedisType.SortedSet:
                        var sortedSetMembers = await db.SortedSetRangeByRankAsync(key, 0, 100);
                        matches = sortedSetMembers.Any(m => m.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }
            catch
            {
                continue;
            }

            if (matches)
            {
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

public class KeyExport
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double? Ttl { get; set; }
    public object? Value { get; set; }
}

public class ConnectionHistory
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime LastUsed { get; set; }
    public string? Name { get; set; }
}

public class SlowLogEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public TimeSpan Duration { get; set; }
    public string Command { get; set; } = string.Empty;
}

public class RedisClientInfo
{
    public long Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AgeSeconds { get; set; }
    public int IdleSeconds { get; set; }
    public int Database { get; set; }
    public string Flags { get; set; } = string.Empty;
}

public class KeyTypeStats
{
    public int Count { get; set; }
    public long TotalMemory { get; set; }
}

public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string? Password { get; set; }
    public int DefaultDatabase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class FavoriteKey
{
    public string Key { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
