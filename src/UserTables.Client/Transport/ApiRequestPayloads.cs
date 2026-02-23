using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UserTables.Client.Transport;

internal sealed class CreateUserTablePayload
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

internal sealed class RowDataPayload
{
    [JsonPropertyName("data")]
    public required IReadOnlyDictionary<string, object?> Data { get; init; }
}

internal sealed class CreateColumnPayload
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("value_type")]
    public required string ValueType { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }
}