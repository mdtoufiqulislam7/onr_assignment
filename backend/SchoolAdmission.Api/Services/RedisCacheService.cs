using System.Text.Json;
using StackExchange.Redis;

namespace SchoolAdmission.Api.Services;

public interface IRedisCacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task RemoveByPrefixAsync(params string[] prefixes);
    Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry);
    Task<bool> ReleaseLockAsync(string key, string value);
    Task EnqueuePayloadAsync<T>(string queueKey, T payload);
}

public class RedisCacheService : IRedisCacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(ILogger<RedisCacheService> logger, IConnectionMultiplexer? redis = null)
    {
        _logger = logger;
        _redis = redis;
    }

    private IDatabase? GetDatabase()
    {
        try
        {
            if (_redis != null && _redis.IsConnected)
            {
                return _redis.GetDatabase();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis connection unavailable. Operating gracefully.");
        }
        return null;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var db = GetDatabase();
        if (db == null) return default;

        try
        {
            var value = await db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return default;
            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cache for key: {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var db = GetDatabase();
        if (db == null) return;

        try
        {
            var serialized = JsonSerializer.Serialize(value);
            await db.StringSetAsync(key, serialized, expiry ?? TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write cache for key: {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(params string[] prefixes)
    {
        if (_redis == null || !_redis.IsConnected) return;

        try
        {
            var endpoints = _redis.GetEndPoints();
            var server = _redis.GetServer(endpoints[0]);
            var db = _redis.GetDatabase();

            foreach (var prefix in prefixes)
            {
                var keys = server.Keys(pattern: $"{prefix}*").ToArray();
                if (keys.Length > 0)
                {
                    await db.KeyDeleteAsync(keys);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete cache keys by prefixes");
        }
    }

    public async Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry)
    {
        var db = GetDatabase();
        if (db == null) return true; // Graceful fallback if Redis is disabled

        try
        {
            return await db.StringSetAsync(key, value, expiry, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acquire distributed lock for key: {Key}", key);
            return true;
        }
    }

    public async Task<bool> ReleaseLockAsync(string key, string value)
    {
        var db = GetDatabase();
        if (db == null) return true;

        try
        {
            const string script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";
            var result = await db.ScriptEvaluateAsync(script, new RedisKey[] { key }, new RedisValue[] { value });
            return (int)result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release lock for key: {Key}", key);
            return false;
        }
    }

    public async Task EnqueuePayloadAsync<T>(string queueKey, T payload)
    {
        var db = GetDatabase();
        if (db == null) return;

        try
        {
            var serialized = JsonSerializer.Serialize(payload);
            await db.ListRightPushAsync(queueKey, serialized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue payload into Redis list: {QueueKey}", queueKey);
        }
    }
}

