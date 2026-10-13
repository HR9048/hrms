using Dapper;
using HrmsBackend.Data;
using HrmsBackend.Models;
using MySqlConnector;

namespace HrmsBackend.Services;

public class ApiRateLimitService
{
    private readonly DbConnectionFactory _db;

    private readonly object _lock = new();

    private Dictionary<string, ApiRateLimitConfig> _configs =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastLoaded = DateTime.MinValue;

    // Reload database configuration every 30 seconds
    private readonly TimeSpan _cacheDuration =
        TimeSpan.FromSeconds(30);

    public ApiRateLimitService(DbConnectionFactory db)
    {
        _db = db;
    }

    public async Task<ApiRateLimitConfig?> GetConfigAsync(
        string path,
        string method)
    {
        await RefreshCacheIfRequiredAsync();

        // First try exact URL + method
        var methodKey = CreateKey(path, method);

        if (_configs.TryGetValue(methodKey, out var config))
        {
            return config;
        }

        // Then try URL with no specific method
        var anyMethodKey = CreateKey(path, "*");

        if (_configs.TryGetValue(anyMethodKey, out config))
        {
            return config;
        }

        return null;
    }

    private async Task RefreshCacheIfRequiredAsync()
    {
        bool shouldRefresh;

        lock (_lock)
        {
            shouldRefresh =
                DateTime.UtcNow - _lastLoaded >= _cacheDuration;
        }

        if (!shouldRefresh)
            return;

        using var connection = _db.CreateConnection();

        const string sql = """
            SELECT
                id AS Id,
                api_url AS ApiUrl,
                http_method AS HttpMethod,
                logged_in AS LoggedIn,
                rate_limit AS RateLimit,
                limit_time AS LimitTime,
                is_active AS IsActive
            FROM master_api_rate_limit
            WHERE is_active = 1
        """;

        var rows = await connection.QueryAsync<ApiRateLimitConfig>(sql);

        var newConfigs =
            new Dictionary<string, ApiRateLimitConfig>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var method =
                string.IsNullOrWhiteSpace(row.HttpMethod)
                    ? "*"
                    : row.HttpMethod.ToUpperInvariant();

            var key = CreateKey(row.ApiUrl, method);

            newConfigs[key] = row;
        }

        lock (_lock)
        {
            _configs = newConfigs;
            _lastLoaded = DateTime.UtcNow;
        }
    }

    private static string CreateKey(
        string path,
        string method)
    {
        path = NormalizePath(path);

        return $"{method.ToUpperInvariant()}:{path}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        path = path.Trim();

        if (!path.StartsWith("/"))
            path = "/" + path;

        // Remove trailing slash except for root
        if (path.Length > 1)
            path = path.TrimEnd('/');

        return path;
    }
}