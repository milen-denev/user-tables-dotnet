using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UserTables.Client.Transport;

internal sealed class ApiUserTablePayload
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class ApiUserTableListPayload
{
    [JsonPropertyName("data")]
    public List<ApiUserTablePayload>? Data { get; init; }
}

internal sealed class ApiUserTableSinglePayload
{
    [JsonPropertyName("data")]
    public ApiUserTablePayload? Data { get; init; }
}

internal sealed class ApiColumnPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("value_type_key")]
    public string? ValueTypeKey { get; init; }

    [JsonPropertyName("value_type")]
    public JsonElement ValueType { get; init; }

    [JsonPropertyName("required")]
    public bool? Required { get; init; }

    [JsonPropertyName("is_auto_relation")]
    public bool? IsAutoRelation { get; init; }
}

internal sealed class ApiColumnListPayload
{
    [JsonPropertyName("data")]
    public List<ApiColumnPayload>? Data { get; init; }
}

internal sealed class ApiColumnSinglePayload
{
    [JsonPropertyName("data")]
    public ApiColumnPayload? Data { get; init; }
}