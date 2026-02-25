using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using UserTables.Client.ChangeTracking;
using UserTables.Client.Internal;
using UserTables.Client.Query;
using UserTables.Client.Transport;

namespace UserTables.Client;

public sealed class UserTableSet<TEntity> where TEntity : class, new()
{
    private readonly UserTablesDbContext _context;
    private readonly EntityMetadata _metadata;
    private readonly QuerySpec _query;
    private readonly bool _tracking;

    internal UserTableSet(UserTablesDbContext context, EntityMetadata metadata, bool tracking, QuerySpec? query = null)
    {
        _context = context;
        _metadata = metadata;
        _tracking = tracking;
        _query = query ?? new QuerySpec();
    }

    public UserTableSet<TEntity> AsNoTracking() => new(_context, _metadata, tracking: false, _query);

    public UserTableSet<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
    {
        var predicates = _query.ClientPredicates.ToList();
        predicates.Add(predicate);

        var translated = PredicateTranslator.TryTranslateFilter(predicate);
        if (translated is not null)
        {
            var resolvedColumn = _metadata.ResolveColumnName(translated.Value.Column);
            var filters = _query.ServerFilters.ToList();
            filters.Add(new ApiFilter(resolvedColumn, translated.Value.Value, translated.Value.Operator));

            return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with
            {
                ServerFilters = filters,
                ServerFilterColumn = filters.Count == 1 ? resolvedColumn : _query.ServerFilterColumn,
                ServerFilterValue = filters.Count == 1 ? translated.Value.Value : _query.ServerFilterValue,
                ServerFilterOperator = filters.Count == 1 ? translated.Value.Operator : _query.ServerFilterOperator,
                ClientPredicates = predicates
            });
        }

        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with { ClientPredicates = predicates });
    }

    public UserTableSet<TEntity> OrderBy(Expression<Func<TEntity, object?>> selector)
    {
        var clientOrder = new List<ClientOrderBy> { new(selector, false) };
        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with
        {
            SortColumn = PredicateTranslator.TranslateOrderBy(selector, _metadata),
            SortDirection = "asc",
            ClientOrderBys = clientOrder
        });
    }

    public UserTableSet<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> selector)
    {
        var clientOrder = new List<ClientOrderBy> { new(selector, true) };
        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with
        {
            SortColumn = PredicateTranslator.TranslateOrderBy(selector, _metadata),
            SortDirection = "desc",
            ClientOrderBys = clientOrder
        });
    }

    public UserTableSet<TEntity> ThenBy(Expression<Func<TEntity, object?>> selector)
    {
        var clientOrder = _query.ClientOrderBys.ToList();
        clientOrder.Add(new ClientOrderBy(selector, false));
        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with { ClientOrderBys = clientOrder });
    }

    public UserTableSet<TEntity> ThenByDescending(Expression<Func<TEntity, object?>> selector)
    {
        var clientOrder = _query.ClientOrderBys.ToList();
        clientOrder.Add(new ClientOrderBy(selector, true));
        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with { ClientOrderBys = clientOrder });
    }

    public UserTableSet<TEntity> Skip(int count) => new(_context, _metadata, _tracking, _query with { Skip = Math.Max(0, count) });
    public UserTableSet<TEntity> Take(int count) => new(_context, _metadata, _tracking, _query with { Take = Math.Max(0, count) });

    public UserTableSet<TEntity> UseAndFilters()
        => new(_context, _metadata, _tracking, _query with { ServerFilterCombinator = "and" });

    public UserTableSet<TEntity> UseOrFilters()
        => new(_context, _metadata, _tracking, _query with { ServerFilterCombinator = "or" });

    public UserTableSet<TEntity> WhereFilters(Func<UserTableFilterBuilder<TEntity>, UserTableFilterBuilder<TEntity>> configure)
    {
        var builder = new UserTableFilterBuilder<TEntity>(_metadata);
        var configured = configure(builder);
        return ApplyBuiltFilters(configured);
    }

    public UserTableSet<TEntity> WhereFilters(Action<UserTableFilterBuilder<TEntity>> configure)
    {
        var builder = new UserTableFilterBuilder<TEntity>(_metadata);
        configure(builder);
        return ApplyBuiltFilters(builder);
    }

    public UserTableSet<TEntity> FilterBy(string column, string op, object? value)
    {
        var filters = _query.ServerFilters.ToList();
        var serialized = PredicateTranslator.ValueToInvariantString(value);
        filters.Add(new ApiFilter(column, serialized, op));

        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with
        {
            ServerFilters = filters,
            ServerFilterColumn = filters.Count == 1 ? column : _query.ServerFilterColumn,
            ServerFilterValue = filters.Count == 1 ? serialized : _query.ServerFilterValue,
            ServerFilterOperator = filters.Count == 1 ? op : _query.ServerFilterOperator
        });
    }

    public UserTableSet<TEntity> WhereEq(string column, object? value) => FilterBy(column, "eq", value);
    public UserTableSet<TEntity> WhereNeq(string column, object? value) => FilterBy(column, "neq", value);
    public UserTableSet<TEntity> WhereGt(string column, object? value) => FilterBy(column, "gt", value);
    public UserTableSet<TEntity> WhereLt(string column, object? value) => FilterBy(column, "lt", value);
    public UserTableSet<TEntity> WhereIn(string column, IEnumerable<object?> values) => FilterBy(column, "in", string.Join(",", values.Select(PredicateTranslator.ValueToInvariantString)));
    public UserTableSet<TEntity> WhereContains(string column, string value) => FilterBy(column, "contains", value);
    public UserTableSet<TEntity> WhereStartsWith(string column, string value) => FilterBy(column, "starts_with", value);

    public async Task<TEntity?> FindAsync(string rowId, CancellationToken cancellationToken = default)
    {
        await _context.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var row = await _context.Transport.GetRowAsync(_metadata.TableId, rowId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return MaterializeEntity(row);
    }

    public async Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default)
    {
        await _context.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var filtersJson = BuildFiltersJson();

        var maxRows = _query.Take is { } take
            ? Math.Max((_query.Skip ?? 0) + take, 0)
            : int.MaxValue;
        var allRows = new List<ApiRow>();
        var page = 1;

        while (true)
        {
            var remaining = maxRows == int.MaxValue ? 100 : Math.Max(0, maxRows - allRows.Count);
            if (remaining == 0)
            {
                break;
            }

            var perPage = Math.Min(100, remaining);
            var rows = await _context.Transport.ListRowsAsync(
                _metadata.TableId,
                new RowPageRequest(
                    page,
                    perPage,
                    _query.ServerFilterColumn,
                    _query.ServerFilterValue,
                    _query.ServerFilterOperator,
                    filtersJson,
                    _query.ServerFilterCombinator,
                    _query.SortColumn,
                    _query.SortDirection),
                cancellationToken).ConfigureAwait(false);

            if (rows.Count == 0)
            {
                break;
            }

            allRows.AddRange(rows);
            if (rows.Count < perPage || allRows.Count >= maxRows)
            {
                break;
            }

            page++;
        }

        IEnumerable<TEntity> result = allRows.Select(MaterializeEntity);

        foreach (var predicateExpression in _query.ClientPredicates)
        {
            var predicate = (Expression<Func<TEntity, bool>>)predicateExpression;
            result = result.Where(predicate.Compile());
        }

        result = ApplyOrdering(result);

        if (_query.Skip is { } skip)
        {
            result = result.Skip(skip);
        }

        if (_query.Take is { } takeCount)
        {
            result = result.Take(takeCount);
        }

        return result.ToList();
    }

    public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var list = await Take(1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => Where(predicate).FirstOrDefaultAsync(cancellationToken);

    public async Task<TEntity> FirstAsync(CancellationToken cancellationToken = default)
    {
        var item = await FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return item ?? throw new InvalidOperationException("Sequence contains no elements.");
    }

    public Task<TEntity> FirstAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => Where(predicate).FirstAsync(cancellationToken);

    public async Task<TEntity?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var list = await Take(2).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.Count switch
        {
            0 => null,
            1 => list[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    public Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => Where(predicate).SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return await FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await Where(predicate).AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _context.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var filtersJson = BuildFiltersJson();

        var compiledPredicates = _query.ClientPredicates
            .Cast<Expression<Func<TEntity, bool>>>()
            .Select(predicate => predicate.Compile())
            .ToArray();

        var total = 0;
        var page = 1;
        while (true)
        {
            var rows = await _context.Transport.ListRowsAsync(
                    _metadata.TableId,
                    new RowPageRequest(
                        page,
                        100,
                        _query.ServerFilterColumn,
                        _query.ServerFilterValue,
                        _query.ServerFilterOperator,
                        filtersJson,
                        _query.ServerFilterCombinator,
                        _query.SortColumn,
                        _query.SortDirection),
                    cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count == 0)
            {
                break;
            }

            if (_query.ClientPredicates.Count == 0)
            {
                total += rows.Count;
            }
            else
            {
                foreach (var row in rows)
                {
                    var entity = (TEntity)_metadata.Materialize(row.Id, row.Data, _context.ContextOptions.JsonSerializerOptions);
                    var matches = compiledPredicates.All(predicate => predicate(entity));
                    if (matches)
                    {
                        total++;
                    }
                }
            }

            if (rows.Count < 100)
            {
                break;
            }

            page++;
        }

        return total;
    }

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var list = await Where(predicate).ToListAsync(cancellationToken).ConfigureAwait(false);
        return list.Count;
    }

    public ValueTask AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Track(entity, _metadata, EntityState.Added);
        return ValueTask.CompletedTask;
    }

    public void Add(TEntity entity)
    {
        _context.ChangeTracker.Track(entity, _metadata, EntityState.Added);
    }

    public void Update(TEntity entity)
    {
        _context.ChangeTracker.Track(entity, _metadata, EntityState.Modified);
    }

    public void Attach(TEntity entity)
    {
        _context.ChangeTracker.Track(entity, _metadata, EntityState.Unchanged, _metadata.ToRowData(entity));
    }

    public void Remove(TEntity entity)
    {
        _context.ChangeTracker.Track(entity, _metadata, EntityState.Deleted);
    }

    private TEntity MaterializeEntity(ApiRow row)
    {
        var entity = (TEntity)_metadata.Materialize(row.Id, row.Data, _context.ContextOptions.JsonSerializerOptions);
        if (_tracking)
        {
            _context.ChangeTracker.Track(entity, _metadata, EntityState.Unchanged, _metadata.ToRowData(entity));
        }

        return entity;
    }

    private IEnumerable<TEntity> ApplyOrdering(IEnumerable<TEntity> source)
    {
        if (_query.ClientOrderBys.Count == 0)
        {
            return source;
        }

        IOrderedEnumerable<TEntity>? ordered = null;
        foreach (var order in _query.ClientOrderBys)
        {
            var selector = (Expression<Func<TEntity, object?>>)order.Selector;
            var keySelector = selector.Compile();

            ordered = ordered is null
                ? (order.Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector))
                : (order.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector));
        }

        return ordered ?? source;
    }

    private string? BuildFiltersJson()
    {
        if (_query.ServerFilters.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(_query.ServerFilters);
    }

    private UserTableSet<TEntity> ApplyBuiltFilters(UserTableFilterBuilder<TEntity> builder)
    {
        var filters = _query.ServerFilters.ToList();
        filters.AddRange(builder.Filters);

        return new UserTableSet<TEntity>(_context, _metadata, _tracking, _query with
        {
            ServerFilters = filters,
            ServerFilterCombinator = builder.Combinator
        });
    }
}