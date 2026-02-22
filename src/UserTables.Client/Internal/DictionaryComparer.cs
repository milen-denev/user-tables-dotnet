using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UserTables.Client.Internal;

internal static class DictionaryComparer
{
    public static bool Equals(IReadOnlyDictionary<string, object?> left, IReadOnlyDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftValue) in left)
        {
            if (!right.TryGetValue(key, out var rightValue))
            {
                return false;
            }

            if (!ScalarEquals(leftValue, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ScalarEquals(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is JsonElement leftElement)
        {
            left = JsonSerializer.Deserialize<object?>(leftElement.GetRawText());
        }

        if (right is JsonElement rightElement)
        {
            right = JsonSerializer.Deserialize<object?>(rightElement.GetRawText());
        }

        return string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
    }
}