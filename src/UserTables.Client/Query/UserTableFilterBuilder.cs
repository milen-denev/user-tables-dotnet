using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using UserTables.Client.Internal;
using UserTables.Client.Transport;

namespace UserTables.Client.Query;

public sealed class UserTableFilterBuilder<TEntity> where TEntity : class, new()
{
    private readonly EntityMetadata _metadata;
    private readonly List<ApiFilter> _filters = [];

    internal UserTableFilterBuilder(EntityMetadata metadata)
    {
        _metadata = metadata;
    }

    internal IReadOnlyList<ApiFilter> Filters => _filters;
    internal string Combinator { get; private set; } = "and";

    public UserTableFilterBuilder<TEntity> And()
    {
        Combinator = "and";
        return this;
    }

    public UserTableFilterBuilder<TEntity> Or()
    {
        Combinator = "or";
        return this;
    }

    public UserTableFilterBuilder<TEntity> Eq(Expression<Func<TEntity, object?>> selector, object? value)
        => Add(selector, "eq", value);

    public UserTableFilterBuilder<TEntity> Neq(Expression<Func<TEntity, object?>> selector, object? value)
        => Add(selector, "neq", value);

    public UserTableFilterBuilder<TEntity> Gt(Expression<Func<TEntity, object?>> selector, object? value)
        => Add(selector, "gt", value);

    public UserTableFilterBuilder<TEntity> Lt(Expression<Func<TEntity, object?>> selector, object? value)
        => Add(selector, "lt", value);

    public UserTableFilterBuilder<TEntity> In(Expression<Func<TEntity, object?>> selector, IEnumerable<object?> values)
        => Add(selector, "in", string.Join(",", values.Select(PredicateTranslator.ValueToInvariantString)));

    public UserTableFilterBuilder<TEntity> Contains(Expression<Func<TEntity, object?>> selector, string value)
        => Add(selector, "contains", value);

    public UserTableFilterBuilder<TEntity> StartsWith(Expression<Func<TEntity, object?>> selector, string value)
        => Add(selector, "starts_with", value);

    private UserTableFilterBuilder<TEntity> Add(Expression<Func<TEntity, object?>> selector, string op, object? value)
    {
        var column = ResolveColumn(selector);
        var serialized = PredicateTranslator.ValueToInvariantString(value);
        _filters.Add(new ApiFilter(column, serialized, op));
        return this;
    }

    private string ResolveColumn(Expression<Func<TEntity, object?>> selector)
    {
        var body = selector.Body is UnaryExpression unary ? unary.Operand : selector.Body;
        if (body is not MemberExpression member)
        {
            throw new InvalidOperationException("Filter selector must target a mapped entity property.");
        }

        return _metadata.ResolveColumnName(member.Member.Name);
    }
}
