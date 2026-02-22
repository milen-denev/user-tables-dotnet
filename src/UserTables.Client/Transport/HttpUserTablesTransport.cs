using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UserTables.Client.Configuration;

namespace UserTables.Client.Transport;

internal sealed class HttpUserTablesTransport : IUserTablesTransport
{
    private readonly HttpClient _httpClient;
    private readonly UserTablesContextOptions _options;

    public HttpUserTablesTransport(UserTablesContextOptions options)
    {
        _options = options;
        _httpClient = options.HttpClient ?? new HttpClient();
    }

    public async Task<ApiRow?> GetRowAsync(string tableId, string rowId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/api/user-tables/{tableId}/rows/{rowId}");
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSingleRow(json.RootElement);
    }

    public async Task<IReadOnlyList<ApiRow>> ListRowsAsync(string tableId, RowPageRequest request, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = request.Page.ToString(CultureInfo.InvariantCulture),
            ["per_page"] = request.PerPage.ToString(CultureInfo.InvariantCulture),
            ["filter_col"] = request.FilterColumn,
            ["filter_value"] = request.FilterValue,
            ["filter_op"] = request.FilterOperator,
            ["filters"] = request.FiltersJson,
            ["filter_combinator"] = request.FilterCombinator,
            ["sort_col"] = request.SortColumn,
            ["sort_dir"] = request.SortDirection
        };

        var path = $"/api/user-tables/{tableId}/rows{BuildQueryString(query)}";
        using var httpRequest = CreateRequest(HttpMethod.Get, path);
        using var response = await SendWithRetryAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseRows(json.RootElement);
    }

    public async Task<ApiRow> CreateRowAsync(string tableId, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/api/user-tables/{tableId}/rows");
        request.Content = BuildJsonContent(new Dictionary<string, object?> { ["data"] = data });

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSingleRow(json.RootElement)
            ?? throw new InvalidOperationException("API response did not include a row payload.");
    }

    public async Task<ApiRow> PatchRowAsync(string tableId, string rowId, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Patch, $"/api/user-tables/{tableId}/rows/{rowId}");
        request.Content = BuildJsonContent(new Dictionary<string, object?> { ["data"] = data });

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSingleRow(json.RootElement)
            ?? throw new InvalidOperationException("API response did not include a row payload.");
    }

    public async Task DeleteRowAsync(string tableId, string rowId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/api/user-tables/{tableId}/rows/{rowId}");
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ApiColumn>> ListColumnsAsync(string tableId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/api/user-tables/{tableId}/columns");
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseColumns(json.RootElement);
    }

    public async Task<ApiColumn> CreateColumnAsync(string tableId, CreateColumnRequest requestModel, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/api/user-tables/{tableId}/columns");
        request.Content = BuildJsonContent(new Dictionary<string, object?>
        {
            ["name"] = requestModel.Name,
            ["description"] = requestModel.Description,
            ["value_type"] = requestModel.ValueType,
            ["required"] = requestModel.Required,
            ["is_active"] = requestModel.IsActive
        });

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseSingleColumn(json.RootElement)
            ?? throw new InvalidOperationException("API response did not include a column payload.");
    }

    public async Task DeleteColumnAsync(string columnId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, $"/api/user-table-columns/{columnId}");
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(0, _options.RetryCount) + 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var clonedRequest = await CloneRequestAsync(request, cancellationToken).ConfigureAwait(false);
                var response = await _httpClient.SendAsync(clonedRequest, cancellationToken).ConfigureAwait(false);
                if (!IsTransientStatus(response.StatusCode) || attempt == attempts)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
            }
            catch (TaskCanceledException) when (attempt < attempts && !cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Unreachable retry state.");
    }

    private TimeSpan BackoffDelay(int attempt)
    {
        var multiplier = Math.Pow(2, attempt - 1);
        var baseMs = _options.RetryBaseDelay.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(baseMs * multiplier);
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout || statusCode == (HttpStatusCode)429 || code >= 500;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var uri = new Uri(_options.BaseUri, relativePath);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        request.Headers.Add("X-API-Key", _options.ApiKey);
        request.Headers.Add("X-Domain-ID", _options.DomainId);
        return request;
    }

    private StringContent BuildJsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _options.JsonSerializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new UserTablesApiException(
            $"User Tables API call failed with status {(int)response.StatusCode} ({response.StatusCode}).",
            response.StatusCode,
            body);
    }

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> query)
    {
        var pairs = query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}")
            .ToArray();

        return pairs.Length == 0 ? string.Empty : $"?{string.Join("&", pairs)}";
    }

    private static IReadOnlyList<ApiRow> ParseRows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!root.TryGetProperty("data", out var dataNode))
        {
            return [];
        }

        if (dataNode.ValueKind == JsonValueKind.Array)
        {
            return dataNode.EnumerateArray().Select(ParseRow).Where(row => row is not null).Cast<ApiRow>().ToArray();
        }

        if (dataNode.ValueKind == JsonValueKind.Object)
        {
            if (dataNode.TryGetProperty("rows", out var rowsNode) && rowsNode.ValueKind == JsonValueKind.Array)
            {
                return rowsNode.EnumerateArray().Select(ParseRow).Where(row => row is not null).Cast<ApiRow>().ToArray();
            }

            if (ParseRow(dataNode) is { } single)
            {
                return [single];
            }
        }

        return [];
    }

    private static ApiRow? ParseSingleRow(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("data", out var dataNode))
        {
            return ParseRow(dataNode);
        }

        return ParseRow(root);
    }

    private static ApiRow? ParseRow(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!node.TryGetProperty("id", out var idNode))
        {
            return null;
        }

        var id = idNode.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var tableId = node.TryGetProperty("user_table_id", out var tableIdNode)
            ? tableIdNode.GetString() ?? string.Empty
            : string.Empty;

        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (node.TryGetProperty("data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in dataNode.EnumerateObject())
            {
                data[property.Name] = property.Value.Clone();
            }
        }

        return new ApiRow(id, tableId, data);
    }

    private static IReadOnlyList<ApiColumn> ParseColumns(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var dataNode))
        {
            return [];
        }

        if (dataNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return dataNode.EnumerateArray()
            .Select(ParseColumn)
            .Where(column => column is not null)
            .Cast<ApiColumn>()
            .ToArray();
    }

    private static ApiColumn? ParseSingleColumn(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("data", out var dataNode))
        {
            return ParseColumn(dataNode);
        }

        return ParseColumn(root);
    }

    private static ApiColumn? ParseColumn(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!node.TryGetProperty("id", out var idNode) || !node.TryGetProperty("name", out var nameNode))
        {
            return null;
        }

        var id = idNode.GetString();
        var name = nameNode.GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var typeKey = node.TryGetProperty("value_type_key", out var keyNode)
            ? keyNode.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(typeKey) && node.TryGetProperty("value_type", out var rawTypeNode))
        {
            typeKey = rawTypeNode.ValueKind switch
            {
                JsonValueKind.String => rawTypeNode.GetString(),
                _ => rawTypeNode.ToString()
            };
        }

        var required = node.TryGetProperty("required", out var requiredNode) && requiredNode.ValueKind == JsonValueKind.True;
        var isAutoRelation = node.TryGetProperty("is_auto_relation", out var autoNode) && autoNode.ValueKind == JsonValueKind.True;

        return new ApiColumn(id!, name!, NormalizeValueType(typeKey), required, isAutoRelation);
    }

    private static string NormalizeValueType(string? valueType)
    {
        return (valueType ?? "text").Trim().ToLowerInvariant() switch
        {
            "int" or "integer" => "number",
            "float" or "double" => "decimal",
            "bool" => "boolean",
            "uuid" => "relation",
            "enum" => "options",
            "textblock" => "text_block",
            "file" => "attachment",
            var known => known
        };
    }
}