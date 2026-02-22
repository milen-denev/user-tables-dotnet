using System;
using System.Collections.Generic;

namespace UserTables.Client.Configuration;

public sealed class UserTablesModelBuilder
{
    private readonly Dictionary<Type, EntityTypeMapBuilder> _builders = new();

    public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class, new()
    {
        var type = typeof(TEntity);
        if (!_builders.TryGetValue(type, out var builder))
        {
            builder = new EntityTypeMapBuilder(type);
            _builders[type] = builder;
        }

        return new EntityTypeBuilder<TEntity>(builder);
    }

    internal IReadOnlyDictionary<Type, EntityTypeMapBuilder> Build() => _builders;
}