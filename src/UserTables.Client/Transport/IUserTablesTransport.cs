using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UserTables.Client.Transport;

internal interface IUserTablesTransport
{
    Task<IReadOnlyList<ApiUserTable>> ListUserTablesAsync(string? search, CancellationToken cancellationToken);
    Task<ApiUserTable> CreateUserTableAsync(CreateUserTableRequest request, CancellationToken cancellationToken);
    Task<ApiRow?> GetRowAsync(string tableId, string rowId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiRow>> ListRowsAsync(string tableId, RowPageRequest request, CancellationToken cancellationToken);
    Task<ApiRow> CreateRowAsync(string tableId, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken);
    Task<ApiRow> PatchRowAsync(string tableId, string rowId, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken);
    Task DeleteRowAsync(string tableId, string rowId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiColumn>> ListColumnsAsync(string tableId, CancellationToken cancellationToken);
    Task<ApiColumn> CreateColumnAsync(string tableId, CreateColumnRequest request, CancellationToken cancellationToken);
    Task DeleteColumnAsync(string columnId, CancellationToken cancellationToken);
}