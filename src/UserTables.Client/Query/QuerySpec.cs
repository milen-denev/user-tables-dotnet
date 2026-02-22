using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using UserTables.Client.Transport;

namespace UserTables.Client.Query;

internal sealed record QuerySpec
{
    public string? ServerFilterColumn { get; init; }
    public string? ServerFilterValue { get; init; }
    public string? ServerFilterOperator { get; init; }
    public string ServerFilterCombinator { get; init; } = "and";
    public IReadOnlyList<ApiFilter> ServerFilters { get; init; } = [];
    public string? SortColumn { get; init; }
    public string SortDirection { get; init; } = "asc";
    public int? Skip { get; init; }
    public int? Take { get; init; }
    public IReadOnlyList<Expression> ClientPredicates { get; init; } = [];
    public IReadOnlyList<ClientOrderBy> ClientOrderBys { get; init; } = [];
}

internal sealed record ClientOrderBy(LambdaExpression Selector, bool Descending);