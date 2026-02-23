# User Tables .NET Client (Strict EF-style)

`UserTables.Client` provides a strict Entity Framework-like API over the Vynflow User Tables JSON API.

## Implemented features

- `UserTablesDbContext` + `UserTableSet<TEntity>` abstraction
- Tracking states: `Added`, `Modified`, `Deleted`, `Unchanged`
- `SaveChangesAsync()` unit-of-work behavior
- EF-like query surface:
  - `Where(...)`
  - `WhereEq(...)`, `WhereNeq(...)`, `WhereGt(...)`, `WhereLt(...)`, `WhereIn(...)`
  - `WhereContains(...)`, `WhereStartsWith(...)`
  - `OrderBy(...)` / `OrderByDescending(...)`
  - `ThenBy(...)` / `ThenByDescending(...)`
  - `Skip(...)` / `Take(...)`
  - `FirstOrDefaultAsync()` / `FirstAsync()`
  - `SingleOrDefaultAsync()`
  - `AnyAsync()` / `CountAsync()`
  - `ToListAsync()` / `FindAsync()`
  - `AsNoTracking()`
- Mapping:
  - `[UserTable("TABLE_ID")]`
  - `[UserTableKey]`
  - `[UserTableColumn("ColumnName")]`
  - Fluent `OnModelCreating` overrides (`ToTable`, `HasKey`, `Property`, `HasColumnName`, `HasConverter`)
- HTTP transport with auth headers (`Authorization`, `X-API-Key`, `X-Domain-ID`)
- Built-in retry/backoff for transient HTTP failures (`408`, `429`, `5xx`, network errors)
- Shared HTTP connection pooling (enabled by default for internally created clients)
- Lightweight object pooling for query-string builders in transport hot path
- Pooled decompression buffers (`ArrayPool<byte>`) in response decode path
- Typed API exception: `UserTablesApiException`
- Optional startup schema auto-migration behind danger flag

## Quick start

```csharp
var options = new UserTablesContextOptionsBuilder()
    .UseBaseUrl("https://vynflow.cloud")
    .UseApiKey("sa-k-vynflow-KEY")
    .UseBearerToken("sa-p-vynflow-SECRET")
    .UseDomainId("DOMAIN_UUID")
    .AllowAutoMigrationDanger(false)
    .UseRetryPolicy(retryCount: 3, baseDelay: TimeSpan.FromMilliseconds(200))
  .UseConnectionPool(
    maxConnectionsPerServer: 128,
    pooledConnectionLifetime: TimeSpan.FromMinutes(10),
    pooledConnectionIdleTimeout: TimeSpan.FromMinutes(2))
    .Build();

await using var db = new MyContext(options);
var activeRows = await db.Leads
  .WhereEq("Active", true)
  .WhereStartsWith("Email", "sales@")
  .UseAndFilters()
  .Take(25)
  .ToListAsync();
```

## Performance and pooling

By default, if you do not pass a custom `HttpClient`, the client reuses a shared pooled `HttpClient` per host + pool settings.

- `UseSharedHttpClientPool(true|false)` toggles shared client pooling (default: `true`)
- `UseConnectionPool(maxConnectionsPerServer, pooledConnectionLifetime, pooledConnectionIdleTimeout)` tunes transport pool behavior
- `UseHttpClient(...)` overrides internal client creation (use this when integrating with `IHttpClientFactory`)

## Auto migration (danger)

When enabled with:

```csharp
.AllowAutoMigrationDanger(true)
```

the context performs schema sync automatically before the first data operation:

- Adds missing columns that exist in your entity mapping
- Removes remote columns not present in your entity mapping
- Recreates columns when value type changed (`delete + create`)

Safer mode:

```csharp
.AllowAutoMigrationDanger(true)
.AutoMigrationAddOnly(true)
```

In add-only mode, destructive operations are skipped:

- No column deletions
- No delete+recreate for type changes
- Only missing columns are added

Notes:

- This is destructive and should be used only in controlled environments.
- Auto relation columns from server-side automation are preserved.
- Latest result is available via `db.LastSchemaMigrationReport`.
- `SchemaMigrationReport.SkippedColumns` shows skipped destructive changes in add-only mode.

## Projects

- `src/UserTables.Client` - library
- `tests/UserTables.Client.Tests` - unit tests
- `samples/UserTables.ConsoleSample` - end-to-end usage sample

## Build

```bash
dotnet build UserTables.slnx
```