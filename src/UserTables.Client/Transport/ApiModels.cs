using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UserTables.Client.Transport;

internal sealed record ApiRow(
    string Id,
    string UserTableId,
    IReadOnlyDictionary<string, object?> Data);

internal sealed record RowPageRequest(
    int Page,
    int PerPage,
    string? FilterColumn,
    string? FilterValue,
    string? FilterOperator,
    string? FiltersJson,
    string? FilterCombinator,
    string? SortColumn,
    string? SortDirection);

internal sealed record ApiFilter(
    [property: JsonPropertyName("column")]
    string Column,
    [property: JsonPropertyName("value")]
    string Value,
    [property: JsonPropertyName("op")]
    string Operator = "eq");

internal sealed record ApiColumn(
    string Id,
    string Name,
    string ValueTypeKey,
    bool Required,
    bool IsAutoRelation);

internal sealed record ApiUserTable(
    string Id,
    string Name);

internal sealed record CreateColumnRequest(
    string Name,
    string ValueType,
    bool Required,
    bool IsActive = true,
    string Description = "");

internal sealed record CreateUserTableRequest(
    string Name,
    string Description = "");

public sealed record SchemaMigrationReport(
    int AddedColumns,
    int RemovedColumns,
    int RecreatedColumns,
    int SkippedColumns = 0)
{
    public int TotalChanges => AddedColumns + RemovedColumns + RecreatedColumns;
}