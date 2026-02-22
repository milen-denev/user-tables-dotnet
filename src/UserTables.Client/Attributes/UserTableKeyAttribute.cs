using System;

namespace UserTables.Client.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class UserTableKeyAttribute : Attribute;