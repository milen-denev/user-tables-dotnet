using System;
using System.Net.Http;
using System.Text.Json;

namespace UserTables.Client.Configuration;

public sealed class UserTablesContextOptionsBuilder
{
    private Uri? _baseUri;
    private string? _apiKey;
    private string? _domainId;
    private string? _bearerToken;
    private HttpClient? _httpClient;
    private JsonSerializerOptions? _jsonSerializerOptions;
    private bool _allowAutoMigrationDanger;
    private bool _autoMigrationAddOnly;
    private int _retryCount = 2;
    private TimeSpan _retryBaseDelay = TimeSpan.FromMilliseconds(200);
    private bool _useSharedHttpClientPool = true;
    private int _maxConnectionsPerServer = 64;
    private TimeSpan _pooledConnectionLifetime = TimeSpan.FromMinutes(10);
    private TimeSpan _pooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);

    public UserTablesContextOptionsBuilder UseBaseUrl(string baseUrl)
    {
        _baseUri = new Uri(baseUrl.TrimEnd('/'), UriKind.Absolute);
        return this;
    }

    public UserTablesContextOptionsBuilder UseApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public UserTablesContextOptionsBuilder UseDomainId(string domainId)
    {
        _domainId = domainId;
        return this;
    }

    public UserTablesContextOptionsBuilder UseBearerToken(string bearerToken)
    {
        _bearerToken = bearerToken;
        return this;
    }

    public UserTablesContextOptionsBuilder UseHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        return this;
    }

    public UserTablesContextOptionsBuilder UseJsonSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        _jsonSerializerOptions = serializerOptions;
        return this;
    }

    public UserTablesContextOptionsBuilder AllowAutoMigrationDanger(bool enabled)
    {
        _allowAutoMigrationDanger = enabled;
        return this;
    }

    public UserTablesContextOptionsBuilder AutoMigrationAddOnly(bool enabled)
    {
        _autoMigrationAddOnly = enabled;
        return this;
    }

    public UserTablesContextOptionsBuilder UseRetryPolicy(int retryCount, TimeSpan baseDelay)
    {
        _retryCount = Math.Max(0, retryCount);
        _retryBaseDelay = baseDelay < TimeSpan.Zero ? TimeSpan.Zero : baseDelay;
        return this;
    }

    public UserTablesContextOptionsBuilder UseSharedHttpClientPool(bool enabled)
    {
        _useSharedHttpClientPool = enabled;
        return this;
    }

    public UserTablesContextOptionsBuilder UseConnectionPool(int maxConnectionsPerServer, TimeSpan pooledConnectionLifetime, TimeSpan pooledConnectionIdleTimeout)
    {
        _maxConnectionsPerServer = Math.Max(1, maxConnectionsPerServer);
        _pooledConnectionLifetime = pooledConnectionLifetime <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : pooledConnectionLifetime;
        _pooledConnectionIdleTimeout = pooledConnectionIdleTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(2) : pooledConnectionIdleTimeout;
        return this;
    }

    public UserTablesContextOptions Build()
    {
        return new UserTablesContextOptions
        {
            BaseUri = _baseUri ?? throw new InvalidOperationException("Base URL is required."),
            ApiKey = _apiKey ?? throw new InvalidOperationException("API key is required."),
            DomainId = _domainId ?? throw new InvalidOperationException("Domain ID is required."),
            BearerToken = _bearerToken ?? throw new InvalidOperationException("Bearer token is required."),
            HttpClient = _httpClient,
            JsonSerializerOptions = _jsonSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web),
            AllowAutoMigrationDanger = _allowAutoMigrationDanger,
            AutoMigrationAddOnly = _autoMigrationAddOnly,
            RetryCount = _retryCount,
            RetryBaseDelay = _retryBaseDelay,
            UseSharedHttpClientPool = _useSharedHttpClientPool,
            MaxConnectionsPerServer = _maxConnectionsPerServer,
            PooledConnectionLifetime = _pooledConnectionLifetime,
            PooledConnectionIdleTimeout = _pooledConnectionIdleTimeout
        };
    }
}