using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.ObjectPool;
using UserTables.Client.Configuration;

namespace UserTables.Client.Transport;

internal sealed class HttpUserTablesTransport : IUserTablesTransport
{
    private static readonly ConcurrentDictionary<string, HttpClient> SharedClientPool = new(StringComparer.Ordinal);
    private static readonly ObjectPoolProvider QueryBuilderPoolProvider = new DefaultObjectPoolProvider();
    private static readonly ObjectPool<StringBuilder> QueryBuilderPool = QueryBuilderPoolProvider.Create(new QueryStringBuilderPooledObjectPolicy());
    private readonly HttpClient _httpClient;
    private readonly UserTablesContextOptions _options;
    private readonly UserTablesTransportJsonContext _jsonContext;

    public HttpUserTablesTransport(UserTablesContextOptions options)
    {
        _options = options;
        _httpClient = options.HttpClient ?? ResolveDefaultHttpClient(options);
        _jsonContext = new UserTablesTransportJsonContext(options.JsonSerializerOptions);
    }

    private static HttpClient ResolveDefaultHttpClient(UserTablesContextOptions options)
    {
        if (!options.UseSharedHttpClientPool)
        {
            return CreateHttpClient(options);
        }

        var key = BuildSharedClientKey(options);
        return SharedClientPool.GetOrAdd(key, _ => CreateHttpClient(options));
    }

    private static string BuildSharedClientKey(UserTablesContextOptions options)
    {
        return string.Join("|",
            options.BaseUri.Scheme,
            options.BaseUri.Host,
            options.BaseUri.Port.ToString(CultureInfo.InvariantCulture),
            options.MaxConnectionsPerServer.ToString(CultureInfo.InvariantCulture),
            options.PooledConnectionLifetime.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
            options.PooledConnectionIdleTimeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpClient CreateHttpClient(UserTablesContextOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            PooledConnectionLifetime = options.PooledConnectionLifetime,
            PooledConnectionIdleTimeout = options.PooledConnectionIdleTimeout,
            MaxConnectionsPerServer = Math.Max(1, options.MaxConnectionsPerServer)
        };

        return new HttpClient(handler, disposeHandler: true);
    }

    public async Task<IReadOnlyList<ApiUserTable>> ListUserTablesAsync(string? search, CancellationToken cancellationToken)
    {
        var path = $"/api/user-tables{BuildListUserTablesQueryString(search)}";
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        var payload = await ParseJsonAsync(response, cancellationToken, _jsonContext.ApiUserTableListPayload).ConfigureAwait(false);
        if (payload?.Data is null || payload.Data.Count == 0)
        {
            return [];
        }

        return payload.Data
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ApiUserTable(item.Id!, item.Name!))
            .ToArray();
    }

    public async Task<ApiUserTable> CreateUserTableAsync(CreateUserTableRequest requestModel, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "/api/user-tables");
        request.Content = BuildJsonContent(new CreateUserTablePayload
        {
            Name = requestModel.Name,
            Description = requestModel.Description
        }, _jsonContext.CreateUserTablePayload);

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        var payload = await ParseJsonAsync(response, cancellationToken, _jsonContext.ApiUserTableSinglePayload).ConfigureAwait(false);
        if (payload?.Data is null || string.IsNullOrWhiteSpace(payload.Data.Id) || string.IsNullOrWhiteSpace(payload.Data.Name))
        {
            throw new InvalidOperationException("API response did not include a user table payload.");
        }

        return new ApiUserTable(payload.Data.Id, payload.Data.Name);
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
        using var json = await ParseJsonDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseSingleRow(json.RootElement);
    }

    public async Task<IReadOnlyList<ApiRow>> ListRowsAsync(string tableId, RowPageRequest request, CancellationToken cancellationToken)
    {
        var path = $"/api/user-tables/{tableId}/rows{BuildListRowsQueryString(request)}";
        using var httpRequest = CreateRequest(HttpMethod.Get, path);
        using var response = await SendWithRetryAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await ParseJsonDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseRows(json.RootElement);
    }

    public async Task<ApiRow> CreateRowAsync(string tableId, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/api/user-tables/{tableId}/rows");
        request.Content = BuildJsonContent(new RowDataPayload { Data = data }, _jsonContext.RowDataPayload);

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await ParseJsonDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseSingleRow(json.RootElement)
            ?? throw new InvalidOperationException("API response did not include a row payload.");
    }

    public async Task<ApiRow> PatchRowAsync(string tableId, string rowId, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Patch, $"/api/user-tables/{tableId}/rows/{rowId}");
        request.Content = BuildJsonContent(new RowDataPayload { Data = data }, _jsonContext.RowDataPayload);

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        using var json = await ParseJsonDocumentAsync(response, cancellationToken).ConfigureAwait(false);
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

        var payload = await ParseJsonAsync(response, cancellationToken, _jsonContext.ApiColumnListPayload).ConfigureAwait(false);
        if (payload?.Data is null || payload.Data.Count == 0)
        {
            return [];
        }

        return payload.Data
            .Select(MapColumnPayload)
            .Where(column => column is not null)
            .Cast<ApiColumn>()
            .ToArray();
    }

    public async Task<ApiColumn> CreateColumnAsync(string tableId, CreateColumnRequest requestModel, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/api/user-tables/{tableId}/columns");
        request.Content = BuildJsonContent(new CreateColumnPayload
        {
            Name = requestModel.Name,
            Description = requestModel.Description,
            ValueType = requestModel.ValueType,
            Required = requestModel.Required,
            IsActive = requestModel.IsActive
        }, _jsonContext.CreateColumnPayload);

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        var payload = await ParseJsonAsync(response, cancellationToken, _jsonContext.ApiColumnSinglePayload).ConfigureAwait(false);
        return MapColumnPayload(payload?.Data)
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
                var response = attempt == 1
                    ? await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false)
                    : await SendRetryAttemptAsync(request, cancellationToken).ConfigureAwait(false);

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

    private async Task<HttpResponseMessage> SendRetryAttemptAsync(HttpRequestMessage originalRequest, CancellationToken cancellationToken)
    {
        using var clonedRequest = await CloneRequestAsync(originalRequest, cancellationToken).ConfigureAwait(false);
        return await _httpClient.SendAsync(clonedRequest, cancellationToken).ConfigureAwait(false);
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

    private static HttpContent BuildJsonContent<TPayload>(TPayload payload, JsonTypeInfo<TPayload> typeInfo)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);
        var content = new ByteArrayContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = Encoding.UTF8.WebName
        };
        return content;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var request = response.RequestMessage;
        var method = request?.Method.Method ?? "UNKNOWN";
        var uri = request?.RequestUri?.ToString() ?? "<unknown>";

        Console.Error.WriteLine(
            $"[UserTables.Client] API failure {(int)response.StatusCode} ({response.StatusCode})\n" +
            $"Request: {method} {uri}\n" +
            $"Response body: {body}");

        throw new UserTablesApiException(
            $"User Tables API call failed with status {(int)response.StatusCode} ({response.StatusCode}) for {method} {uri}.",
            response.StatusCode,
            body);
    }

    private static async Task<JsonDocument> ParseJsonDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var decoded = DecodeResponsePayload(payload, response.Content.Headers.ContentEncoding);
        return JsonDocument.Parse(decoded);
    }

    private static async Task<T?> ParseJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken, JsonTypeInfo<T> typeInfo)
    {
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var decoded = DecodeResponsePayload(payload, response.Content.Headers.ContentEncoding);
        return JsonSerializer.Deserialize(decoded, typeInfo);
    }

    private static ApiColumn? MapColumnPayload(ApiColumnPayload? payload)
    {
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Name))
        {
            return null;
        }

        var typeKey = payload.ValueTypeKey;
        if (string.IsNullOrWhiteSpace(typeKey) && payload.ValueType.ValueKind != JsonValueKind.Undefined)
        {
            typeKey = payload.ValueType.ValueKind switch
            {
                JsonValueKind.String => payload.ValueType.GetString(),
                _ => payload.ValueType.ToString()
            };
        }

        return new ApiColumn(
            payload.Id,
            payload.Name,
            NormalizeValueType(typeKey),
            payload.Required == true,
            payload.IsAutoRelation == true);
    }

    private static byte[] DecodeResponsePayload(byte[] payload, ICollection<string> encodings)
    {
        if (payload.Length == 0)
        {
            return payload;
        }

        var decoded = payload;
        var applied = encodings
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToArray();

        if (applied.Length > 0)
        {
            foreach (var encoding in applied.Reverse())
            {
                decoded = TryDecodeByEncoding(decoded, encoding);
            }

            return decoded;
        }

        if (decoded.Length >= 2 && decoded[0] == 0x1F && decoded[1] == 0x8B)
        {
            return TryDecodeByEncoding(decoded, "gzip");
        }

        return decoded;
    }

    private static byte[] TryDecodeByEncoding(byte[] payload, string encoding)
    {
        try
        {
            return encoding switch
            {
                "gzip" => Decompress(payload, input => new GZipStream(input, CompressionMode.Decompress)),
                "deflate" => Decompress(payload, input => new DeflateStream(input, CompressionMode.Decompress)),
                "br" => Decompress(payload, input => new BrotliStream(input, CompressionMode.Decompress)),
                _ => payload
            };
        }
        catch (InvalidDataException)
        {
            return payload;
        }
    }

    private static byte[] Decompress(byte[] payload, Func<Stream, Stream> streamFactory)
    {
        using var input = new MemoryStream(payload);
        using var decoded = streamFactory(input);
        using var output = new MemoryStream();

        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = decoded.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.ToArray();
    }

    private static string BuildListUserTablesQueryString(string? search)
    {
        var builder = RentQueryBuilder();
        try
        {
            var hasAny = false;
            AppendQueryPair(builder, ref hasAny, "search", search);
            AppendQueryPair(builder, ref hasAny, "page", "1");
            AppendQueryPair(builder, ref hasAny, "per_page", "100");
            return hasAny ? builder.ToString() : string.Empty;
        }
        finally
        {
            ReturnQueryBuilder(builder);
        }
    }

    private static string BuildListRowsQueryString(RowPageRequest request)
    {
        var builder = RentQueryBuilder();
        try
        {
            var hasAny = false;
            AppendQueryPair(builder, ref hasAny, "page", request.Page.ToString(CultureInfo.InvariantCulture));
            AppendQueryPair(builder, ref hasAny, "per_page", request.PerPage.ToString(CultureInfo.InvariantCulture));
            AppendQueryPair(builder, ref hasAny, "filter_col", request.FilterColumn);
            AppendQueryPair(builder, ref hasAny, "filter_value", request.FilterValue);
            AppendQueryPair(builder, ref hasAny, "filter_op", request.FilterOperator);
            AppendQueryPair(builder, ref hasAny, "filters", request.FiltersJson);
            AppendQueryPair(builder, ref hasAny, "filter_combinator", request.FilterCombinator);
            AppendQueryPair(builder, ref hasAny, "sort_col", request.SortColumn);
            AppendQueryPair(builder, ref hasAny, "sort_dir", request.SortDirection);
            return hasAny ? builder.ToString() : string.Empty;
        }
        finally
        {
            ReturnQueryBuilder(builder);
        }
    }

    private static void AppendQueryPair(StringBuilder builder, ref bool hasAny, string key, string? value)
    {
        if (value is null)
        {
            return;
        }

        var valueSpan = value.AsSpan().Trim();
        if (valueSpan.IsEmpty)
        {
            return;
        }

        builder.Append(hasAny ? '&' : '?');
        builder.Append(Uri.EscapeDataString(key));
        builder.Append('=');
        builder.Append(Uri.EscapeDataString(valueSpan));
        hasAny = true;
    }

    private static StringBuilder RentQueryBuilder()
    {
        return QueryBuilderPool.Get();
    }

    private static void ReturnQueryBuilder(StringBuilder builder)
    {
        QueryBuilderPool.Return(builder);
    }

    private sealed class QueryStringBuilderPooledObjectPolicy : PooledObjectPolicy<StringBuilder>
    {
        public override StringBuilder Create()
        {
            return new StringBuilder(256);
        }

        public override bool Return(StringBuilder obj)
        {
            if (obj.Capacity > 8 * 1024)
            {
                return false;
            }

            obj.Clear();
            return true;
        }
    }

    private static IReadOnlyList<ApiUserTable> ParseUserTables(JsonElement root)
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
            .Select(ParseUserTable)
            .Where(table => table is not null)
            .Cast<ApiUserTable>()
            .ToArray();
    }

    private static ApiUserTable? ParseSingleUserTable(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("data", out var dataNode))
        {
            return ParseUserTable(dataNode);
        }

        return ParseUserTable(root);
    }

    private static ApiUserTable? ParseUserTable(JsonElement node)
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

        return new ApiUserTable(id!, name!);
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