using System;
using System.Globalization;
using System.Linq.Expressions;

namespace UserTables.Client.Query;

internal static class PredicateTranslator
{
    public static (string Column, string Value)? TryTranslateFilter<TEntity>(Expression<Func<TEntity, bool>> predicate)
    {
        if (TryTranslateExpression(predicate.Body, out var translated))
        {
            return translated;
        }

        if (TryGetMember(predicate.Body, out var memberName))
        {
            return (memberName, "true");
        }

        return null;
    }

    private static bool TryTranslateExpression(Expression expression, out (string Column, string Value) translated)
    {
        if (expression is BinaryExpression binary && binary.NodeType == ExpressionType.Equal)
        {
            if (TryGetMember(binary.Left, out var leftMember) && TryGetConstant(binary.Right, out var rightValue))
            {
                translated = (leftMember, ToInvariantString(rightValue));
                return true;
            }

            if (TryGetMember(binary.Right, out var rightMember) && TryGetConstant(binary.Left, out var leftValue))
            {
                translated = (rightMember, ToInvariantString(leftValue));
                return true;
            }
        }

        if (expression is BinaryExpression andAlso && andAlso.NodeType == ExpressionType.AndAlso)
        {
            if (TryTranslateExpression(andAlso.Left, out translated))
            {
                return true;
            }

            if (TryTranslateExpression(andAlso.Right, out translated))
            {
                return true;
            }
        }

        translated = default;
        return false;
    }

    public static string TranslateOrderBy<TEntity>(Expression<Func<TEntity, object?>> selector)
    {
        var body = selector.Body is UnaryExpression unary ? unary.Operand : selector.Body;
        if (body is not MemberExpression member)
        {
            throw new InvalidOperationException("OrderBy expressions must target a mapped property.");
        }

        return member.Member.Name;
    }

    private static bool TryGetMember(Expression expression, out string memberName)
    {
        var body = expression is UnaryExpression unary ? unary.Operand : expression;
        if (body is MemberExpression member && member.Expression is ParameterExpression)
        {
            memberName = member.Member.Name;
            return true;
        }

        memberName = string.Empty;
        return false;
    }

    private static bool TryGetConstant(Expression expression, out object? value)
    {
        if (expression is ConstantExpression constant)
        {
            value = constant.Value;
            return true;
        }

        try
        {
            var lambda = Expression.Lambda(expression);
            value = lambda.Compile().DynamicInvoke();
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    internal static string ValueToInvariantString(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string ToInvariantString(object? value) => ValueToInvariantString(value);
}