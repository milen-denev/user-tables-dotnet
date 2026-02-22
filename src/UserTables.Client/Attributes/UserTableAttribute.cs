using System;

namespace UserTables.Client.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class UserTableAttribute(string tableId) : Attribute
{
    public string TableId { get; } = tableId;
}