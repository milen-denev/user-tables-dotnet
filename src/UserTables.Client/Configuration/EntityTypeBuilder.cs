using System;
using System.Linq.Expressions;
using System.Reflection;

namespace UserTables.Client.Configuration;

public sealed class EntityTypeBuilder<TEntity> where TEntity : class, new()
{
    private readonly EntityTypeMapBuilder _builder;

    internal EntityTypeBuilder(EntityTypeMapBuilder builder)
    {
        _builder = builder;
    }

    public EntityTypeBuilder<TEntity> ToTable(string tableId)
    {
        _builder.TableId = tableId;
        return this;
    }

    public EntityTypeBuilder<TEntity> HasKey(Expression<Func<TEntity, object?>> keyExpression)
    {
        _builder.KeyProperty = ExtractProperty(keyExpression);
        return this;
    }

    public PropertyBuilder<TEntity> Property(Expression<Func<TEntity, object?>> propertyExpression)
    {
        var property = ExtractProperty(propertyExpression);
        return new PropertyBuilder<TEntity>(_builder, property);
    }

    internal static PropertyInfo ExtractProperty(Expression<Func<TEntity, object?>> expression)
    {
        var body = expression.Body is UnaryExpression unary ? unary.Operand : expression.Body;
        if (body is MemberExpression { Member: PropertyInfo property })
        {
            return property;
        }

        throw new InvalidOperationException($"Expression '{expression}' is not a valid property expression.");
    }
}

public sealed class PropertyBuilder<TEntity> where TEntity : class, new()
{
    private readonly EntityTypeMapBuilder _builder;
    private readonly PropertyInfo _property;

    internal PropertyBuilder(EntityTypeMapBuilder builder, PropertyInfo property)
    {
        _builder = builder;
        _property = property;
    }

    public PropertyBuilder<TEntity> HasColumnName(string columnName)
    {
        _builder.GetOrAddProperty(_property).ColumnName = columnName;
        return this;
    }

    public PropertyBuilder<TEntity> HasConverter(ValueConverter converter)
    {
        _builder.GetOrAddProperty(_property).Converter = converter;
        return this;
    }

    public PropertyBuilder<TEntity> HasValueType(UserTableColumnValueType valueType)
    {
        _builder.GetOrAddProperty(_property).ValueType = valueType;
        return this;
    }
}