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
internal sealed partial class UserTablesTransportJsonContext : JsonSerializerContext
{
}