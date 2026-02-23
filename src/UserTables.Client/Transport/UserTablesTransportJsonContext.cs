using System.Text.Json.Serialization;

namespace UserTables.Client.Transport;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(CreateUserTablePayload))]
[JsonSerializable(typeof(RowDataPayload))]
[JsonSerializable(typeof(CreateColumnPayload))]
[JsonSerializable(typeof(ApiUserTableListPayload))]
[JsonSerializable(typeof(ApiUserTableSinglePayload))]
[JsonSerializable(typeof(ApiColumnListPayload))]
[JsonSerializable(typeof(ApiColumnSinglePayload))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
internal sealed partial class UserTablesTransportJsonContext : JsonSerializerContext
{
}