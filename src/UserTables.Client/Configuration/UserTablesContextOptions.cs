using System;
using System.Net.Http;
using System.Text.Json;

namespace UserTables.Client.Configuration;

public sealed class UserTablesContextOptions
{
    public required Uri BaseUri { get; init; }
    public required string ApiKey { get; init; }
    public required string DomainId { get; init; }
    public required string BearerToken { get; init; }
    public HttpClient? HttpClient { get; init; }
    public JsonSerializerOptions JsonSerializerOptions { get; init; } = new(JsonSerializerDefaults.Web);
    public bool AllowAutoMigrationDanger { get; init; }
    public bool AutoMigrationAddOnly { get; init; }
    public int RetryCount { get; init; } = 2;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public bool UseSharedHttpClientPool { get; init; } = true;
    public int MaxConnectionsPerServer { get; init; } = 64;
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
}