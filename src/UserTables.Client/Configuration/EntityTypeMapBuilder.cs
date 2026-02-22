using System;
using System.Collections.Generic;
using System.Reflection;

namespace UserTables.Client.Configuration;

internal sealed class EntityTypeMapBuilder(Type entityType)
{
    private readonly Dictionary<string, PropertyMapBuilder> _properties = new(StringComparer.Ordinal);

    public Type EntityType { get; } = entityType;
    public string? TableId { get; set; }
    public PropertyInfo? KeyProperty { get; set; }

    public IReadOnlyDictionary<string, PropertyMapBuilder> Properties => _properties;

    public PropertyMapBuilder GetOrAddProperty(PropertyInfo property)
    {
        if (!_properties.TryGetValue(property.Name, out var map))
        {
            map = new PropertyMapBuilder(property);
            _properties[property.Name] = map;
        }

        return map;
    }
}

internal sealed class PropertyMapBuilder(PropertyInfo property)
{
    public PropertyInfo Property { get; } = property;
    public string? ColumnName { get; set; }
    public ValueConverter? Converter { get; set; }
    public UserTableColumnValueType? ValueType { get; set; }
}