using System;

namespace UserTables.Client.Configuration;

public sealed class ValueConverter(Func<object?, object?> toProvider, Func<object?, object?> fromProvider)
{
    public object? ToProvider(object? value) => toProvider(value);
    public object? FromProvider(object? value) => fromProvider(value);
}