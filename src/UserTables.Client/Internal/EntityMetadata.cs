using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UserTables.Client.Attributes;
using UserTables.Client.Configuration;

namespace UserTables.Client.Internal;

internal sealed class EntityMetadata
{
    private static readonly Type[] ScalarTypes =
    [
        typeof(string),
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid)
    ];

    private readonly IReadOnlyList<PropertyMetadata> _properties;
    private readonly IReadOnlyDictionary<string, string> _propertyToColumn;
    private readonly JsonSerializerOptions _jsonOptions;

    public EntityMetadata(Type entityType, string tableId, PropertyInfo keyProperty, IReadOnlyList<PropertyMetadata> properties, JsonSerializerOptions jsonOptions)
    {
        EntityType = entityType;
        TableId = tableId;
        KeyProperty = keyProperty;
        _properties = properties;
        _jsonOptions = jsonOptions;
        _propertyToColumn = properties.ToDictionary(
            property => property.Property.Name,
            property => property.ColumnName,
            StringComparer.Ordinal);
    }

    public Type EntityType { get; }
    public string TableId { get; private set; }
    public PropertyInfo KeyProperty { get; }

    public IReadOnlyList<ExpectedColumnSchema> ExpectedColumns => _properties
        .Select(property => new ExpectedColumnSchema(property.ColumnName, property.ValueTypeKey, property.Required))
        .ToArray();

    public string? GetRowId(object entity)
    {
        return KeyProperty.GetValue(entity)?.ToString();
    }

    public string ResolveColumnName(string propertyName)
    {
        return _propertyToColumn.TryGetValue(propertyName, out var column)
            ? column
            : propertyName;
    }

    public void SetRowId(object entity, string rowId)
    {
        if (!KeyProperty.CanWrite)
        {
            return;
        }

        if (KeyProperty.PropertyType == typeof(string))
        {
            KeyProperty.SetValue(entity, rowId);
            return;
        }

        if (KeyProperty.PropertyType == typeof(Guid) && Guid.TryParse(rowId, out var guid))
        {
            KeyProperty.SetValue(entity, guid);
        }
    }

    public void SetTableId(string tableId)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            throw new ArgumentException("Table id cannot be null or whitespace.", nameof(tableId));
        }

        TableId = tableId;
    }

    public Dictionary<string, object?> ToRowData(object entity)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in _properties)
        {
            var value = property.Property.GetValue(entity);
            if (property.Converter is not null)
            {
                row[property.ColumnName] = property.Converter.ToProvider(value);
                continue;
            }

            if (property.AutoJson && value is not null)
            {
                row[property.ColumnName] = JsonSerializer.Serialize(value, property.Property.PropertyType, _jsonOptions);
                continue;
            }

            var targetType = Nullable.GetUnderlyingType(property.Property.PropertyType) ?? property.Property.PropertyType;
            if (targetType.IsEnum && value is not null)
            {
                row[property.ColumnName] = value.ToString();
                continue;
            }

            row[property.ColumnName] = value;
        }

        return row;
    }

    public object Materialize(string rowId, IReadOnlyDictionary<string, object?> rowData, JsonSerializerOptions jsonOptions)
    {
        var instance = Activator.CreateInstance(EntityType)
            ?? throw new InvalidOperationException($"Could not instantiate {EntityType.Name}. Ensure it has a parameterless constructor.");

        foreach (var property in _properties)
        {
            if (!rowData.TryGetValue(property.ColumnName, out var value))
            {
                continue;
            }

            var normalized = property.Converter?.FromProvider(value)
                ?? ConvertToPropertyType(property.Property.PropertyType, value, jsonOptions, property.AutoJson);
            if (property.Property.CanWrite)
            {
                property.Property.SetValue(instance, normalized);
            }
        }

        SetRowId(instance, rowId);
        return instance;
    }

    public static EntityMetadata Build(Type entityType, IReadOnlyDictionary<Type, EntityTypeMapBuilder> configuredMaps, JsonSerializerOptions jsonOptions)
    {
        configuredMaps.TryGetValue(entityType, out var configured);
        var tableAttr = entityType.GetCustomAttribute<UserTableAttribute>();
        var tableId = configured?.TableId ?? tableAttr?.TableId;
        if (string.IsNullOrWhiteSpace(tableId))
        {
            throw new InvalidOperationException($"Entity {entityType.Name} has no table mapping. Use [UserTable(" + "\"TABLE_ID\"" + ")] or modelBuilder.Entity<>().ToTable(...).");
        }

        var props = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .ToList();

        var keyProperty = configured?.KeyProperty
            ?? props.FirstOrDefault(property => property.GetCustomAttribute<UserTableKeyAttribute>() is not null)
            ?? props.FirstOrDefault(property => string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Entity {entityType.Name} has no key property. Use [UserTableKey] or HasKey(...).");

        var propertyMaps = new List<PropertyMetadata>();
        var nullability = new NullabilityInfoContext();
        foreach (var property in props.Where(property => property != keyProperty))
        {
            PropertyMapBuilder? configuredProperty = null;
            configured?.Properties.TryGetValue(property.Name, out configuredProperty);
            var columnAttr = property.GetCustomAttribute<UserTableColumnAttribute>();
            var columnName = configuredProperty?.ColumnName ?? columnAttr?.ColumnName ?? property.Name;
            var valueType = configuredProperty?.ValueType ?? InferValueType(property.PropertyType);
            var required = IsRequiredProperty(property, nullability);
            var autoJson = configuredProperty?.Converter is null && ShouldAutoJsonConvert(property.PropertyType);
            propertyMaps.Add(new PropertyMetadata(property, columnName, configuredProperty?.Converter, ValueTypeToApiKey(valueType), required, autoJson));
        }

        return new EntityMetadata(entityType, tableId!, keyProperty, propertyMaps, jsonOptions);
    }

    private static object? ConvertToPropertyType(Type propertyType, object? value, JsonSerializerOptions options, bool autoJson)
    {
        if (value is null)
        {
            return null;
        }

        if (autoJson && value is string jsonText && !string.IsNullOrWhiteSpace(jsonText))
        {
            return JsonSerializer.Deserialize(jsonText, propertyType, options);
        }

        if (value is JsonElement element)
        {
            if (autoJson && element.ValueKind == JsonValueKind.String)
            {
                var elementJsonText = element.GetString();
                if (!string.IsNullOrWhiteSpace(elementJsonText))
                {
                    return JsonSerializer.Deserialize(elementJsonText, propertyType, options);
                }

                return null;
            }

            var targetEnumType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetEnumType.IsEnum && element.ValueKind == JsonValueKind.String)
            {
                var enumName = element.GetString();
                if (!string.IsNullOrWhiteSpace(enumName))
                {
                    return Enum.Parse(targetEnumType, enumName, ignoreCase: true);
                }

                return null;
            }

            return JsonSerializer.Deserialize(element.GetRawText(), propertyType, options);
        }

        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType.IsAssignableFrom(value.GetType()))
        {
            return value;
        }

        if (targetType.IsEnum)
        {
            return value is string enumName
                ? Enum.Parse(targetType, enumName, ignoreCase: true)
                : Enum.ToObject(targetType, value);
        }

        return Convert.ChangeType(value, targetType);
    }

    private static UserTableColumnValueType InferValueType(Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (targetType == typeof(bool))
        {
            return UserTableColumnValueType.Boolean;
        }

        if (targetType == typeof(int) || targetType == typeof(long) || targetType == typeof(short) ||
            targetType == typeof(uint) || targetType == typeof(ulong) || targetType == typeof(ushort))
        {
            return UserTableColumnValueType.Number;
        }

        if (targetType == typeof(float) || targetType == typeof(double) || targetType == typeof(decimal))
        {
            return UserTableColumnValueType.Decimal;
        }

        if (targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset))
        {
            return UserTableColumnValueType.Date;
        }

        if (targetType == typeof(Guid))
        {
            return UserTableColumnValueType.Text;
        }

        return UserTableColumnValueType.Text;
    }

    private static bool ShouldAutoJsonConvert(Type propertyType)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (targetType.IsEnum)
        {
            return false;
        }

        if (ScalarTypes.Contains(targetType))
        {
            return false;
        }

        return true;
    }

    private static bool IsRequiredProperty(PropertyInfo property, NullabilityInfoContext nullability)
    {
        var type = property.PropertyType;
        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is null;
        }

        var info = nullability.Create(property);
        return info.WriteState == NullabilityState.NotNull || info.ReadState == NullabilityState.NotNull;
    }

    private static string ValueTypeToApiKey(UserTableColumnValueType valueType)
    {
        return valueType switch
        {
            UserTableColumnValueType.Number => "number",
            UserTableColumnValueType.Decimal => "decimal",
            UserTableColumnValueType.Date => "date",
            UserTableColumnValueType.Boolean => "boolean",
            UserTableColumnValueType.Relation => "relation",
            UserTableColumnValueType.Options => "options",
            UserTableColumnValueType.TextBlock => "text_block",
            UserTableColumnValueType.Attachment => "attachment",
            _ => "text"
        };
    }
}

internal sealed class PropertyMetadata(PropertyInfo property, string columnName, ValueConverter? converter, string valueTypeKey, bool required, bool autoJson)
{
    public PropertyInfo Property { get; } = property;
    public string ColumnName { get; } = columnName;
    public ValueConverter? Converter { get; } = converter;
    public string ValueTypeKey { get; } = valueTypeKey;
    public bool Required { get; } = required;
    public bool AutoJson { get; } = autoJson;
}

internal sealed record ExpectedColumnSchema(string Name, string ValueTypeKey, bool Required);