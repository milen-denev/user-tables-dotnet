using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UserTables.Client.ChangeTracking;
using UserTables.Client.Configuration;
using UserTables.Client.Internal;
using UserTables.Client.Transport;

namespace UserTables.Client;

public abstract class UserTablesDbContext : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Type, EntityMetadata> _metadataCache = new();
    private readonly IReadOnlyDictionary<Type, EntityTypeMapBuilder> _model;
    private readonly IUserTablesTransport _transport;
    private readonly object _initializationGate = new();
    private Task<SchemaMigrationReport>? _initializationTask;

    protected UserTablesDbContext(UserTablesContextOptions options)
    {
        ContextOptions = options;
        ChangeTracker = new ChangeTracker();

        var modelBuilder = new UserTablesModelBuilder();
        OnModelCreating(modelBuilder);
        _model = modelBuilder.Build();
        _transport = new HttpUserTablesTransport(options);
    }

    internal UserTablesContextOptions ContextOptions { get; }
    public ChangeTracker ChangeTracker { get; }
    public SchemaMigrationReport? LastSchemaMigrationReport { get; private set; }

    protected virtual void OnModelCreating(UserTablesModelBuilder modelBuilder)
    {
    }

    public UserTableSet<TEntity> Set<TEntity>() where TEntity : class, new()
    {
        return new UserTableSet<TEntity>(this, ResolveMetadata(typeof(TEntity)), tracking: true);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        ChangeTracker.DetectChanges();
        var pending = ChangeTracker.Entries
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        var affected = 0;
        foreach (var entry in pending)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                {
                    var created = await _transport.CreateRowAsync(entry.Metadata.TableId, entry.Metadata.ToRowData(entry.Entity), cancellationToken).ConfigureAwait(false);
                    entry.Metadata.SetRowId(entry.Entity, created.Id);
                    affected++;
                    break;
                }
                case EntityState.Modified:
                {
                    var rowId = entry.Metadata.GetRowId(entry.Entity)
                        ?? throw new InvalidOperationException($"Entity {entry.EntityType.Name} is missing key value for update.");
                    await _transport.PatchRowAsync(entry.Metadata.TableId, rowId, entry.Metadata.ToRowData(entry.Entity), cancellationToken).ConfigureAwait(false);
                    affected++;
                    break;
                }
                case EntityState.Deleted:
                {
                    var rowId = entry.Metadata.GetRowId(entry.Entity)
                        ?? throw new InvalidOperationException($"Entity {entry.EntityType.Name} is missing key value for delete.");
                    await _transport.DeleteRowAsync(entry.Metadata.TableId, rowId, cancellationToken).ConfigureAwait(false);
                    affected++;
                    break;
                }
            }
        }

        ChangeTracker.AcceptAllChanges();
        return affected;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async Task<SchemaMigrationReport> EnsureAutoMigratedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        LastSchemaMigrationReport = await ExecuteAutoMigration(cancellationToken).ConfigureAwait(false);
        return LastSchemaMigrationReport;
    }

    internal async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!ContextOptions.AllowAutoMigrationDanger)
        {
            return;
        }

        var initTask = _initializationTask;
        if (initTask is null)
        {
            lock (_initializationGate)
            {
                _initializationTask ??= ExecuteAutoMigration(CancellationToken.None);
                initTask = _initializationTask;
            }
        }

        LastSchemaMigrationReport = await initTask!.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal EntityMetadata ResolveMetadata(Type entityType)
    {
        return _metadataCache.GetOrAdd(entityType, type => EntityMetadata.Build(type, _model));
    }

    internal IUserTablesTransport Transport => _transport;

    private async Task<SchemaMigrationReport> ExecuteAutoMigration(CancellationToken cancellationToken)
    {
        var entityTypes = DiscoverEntityTypes();
        if (entityTypes.Count == 0)
        {
            return new SchemaMigrationReport(0, 0, 0, 0);
        }

        var metadata = entityTypes
            .Select(ResolveMetadata)
            .Where(item => item.ExpectedColumns.Count > 0)
            .ToArray();

        if (metadata.Length == 0)
        {
            return new SchemaMigrationReport(0, 0, 0, 0);
        }

        var migrator = new SchemaAutoMigrator(_transport);
        return await migrator
            .MigrateAsync(metadata, ContextOptions.AutoMigrationAddOnly, cancellationToken)
            .ConfigureAwait(false);
    }

    private HashSet<Type> DiscoverEntityTypes()
    {
        var entities = new HashSet<Type>(_model.Keys);
        var dbSetType = typeof(UserTableSet<>);

        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (!property.PropertyType.IsGenericType)
            {
                continue;
            }

            if (property.PropertyType.GetGenericTypeDefinition() != dbSetType)
            {
                continue;
            }

            var entityType = property.PropertyType.GetGenericArguments()[0];
            entities.Add(entityType);
        }

        return entities;
    }
}