using UserTables.Client.Query;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using UserTables.Client.Attributes;
using UserTables.Client.Configuration;
using UserTables.Client.Internal;
using UserTables.Client.Transport;

namespace UserTables.Client.Tests;

public class PredicateTranslatorTests
{
    [Fact]
    public void ApiFilter_Serializes_To_Backend_Field_Names()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new UserTables.Client.Transport.ApiFilter("Active", "true", "eq"),
            new UserTables.Client.Transport.ApiFilter("Email", "sales@", "starts_with")
        });

        Assert.Equal("[{\"column\":\"Active\",\"value\":\"true\",\"op\":\"eq\"},{\"column\":\"Email\",\"value\":\"sales@\",\"op\":\"starts_with\"}]", json);
    }

    [Fact]
    public void Translates_Equality_Expression_To_Server_Filter()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => lead.Name == "Acme";

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.NotNull(translated);
        Assert.Equal("Name", translated?.Column);
        Assert.Equal("Acme", translated?.Value);
        Assert.Equal("eq", translated?.Operator);
    }

    [Fact]
    public void Returns_Null_For_NonEquality_Expression()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => lead.Name.Contains("Ac");

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.Null(translated);
    }

    [Fact]
    public void Translates_First_Equality_From_AndAlso_Expression()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => lead.Active && lead.Name == "Acme";

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.NotNull(translated);
        Assert.Equal("Name", translated?.Column);
        Assert.Equal("Acme", translated?.Value);
        Assert.Equal("eq", translated?.Operator);
    }

    [Fact]
    public void Translates_Greater_Than_Expression_To_Server_Filter()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => lead.YearlyRevenue > 179_900_000;

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.NotNull(translated);
        Assert.Equal("YearlyRevenue", translated?.Column);
        Assert.Equal("179900000", translated?.Value);
        Assert.Equal("gt", translated?.Operator);
    }

    [Fact]
    public void Translates_Reversed_Greater_Than_Expression_To_Server_Filter()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => 179_900_000 < lead.YearlyRevenue;

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.NotNull(translated);
        Assert.Equal("YearlyRevenue", translated?.Column);
        Assert.Equal("179900000", translated?.Value);
        Assert.Equal("gt", translated?.Operator);
    }

    [Fact]
    public void Translates_Not_Equal_Expression_To_Server_Filter()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => lead.Active != true;

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.NotNull(translated);
        Assert.Equal("Active", translated?.Column);
        Assert.Equal("True", translated?.Value);
        Assert.Equal("neq", translated?.Operator);
    }

    [Fact]
    public void Translates_OrderBy_Selector()
    {
        Expression<Func<LeadEntity, object?>> selector = lead => lead.Email;

        var translated = PredicateTranslator.TranslateOrderBy(selector);

        Assert.Equal("Email", translated);
    }

    private sealed class LeadEntity
    {
        public required string Name { get; set; }
        public required string Email { get; set; }
        public bool Active { get; set; }
        public long YearlyRevenue { get; set; }
    }
}

public class UserTablesContextOptionsBuilderTests
{
    [Fact]
    public void Build_Uses_Default_Pooling_Settings()
    {
        var options = new UserTablesContextOptionsBuilder()
            .UseBaseUrl("https://example.com")
            .UseApiKey("api-key")
            .UseBearerToken("token")
            .UseDomainId("domain-id")
            .Build();

        Assert.True(options.UseSharedHttpClientPool);
        Assert.Equal(64, options.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromMinutes(10), options.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromMinutes(2), options.PooledConnectionIdleTimeout);
    }

    [Fact]
    public void Build_Respects_Configured_Pooling_Settings()
    {
        var options = new UserTablesContextOptionsBuilder()
            .UseBaseUrl("https://example.com")
            .UseApiKey("api-key")
            .UseBearerToken("token")
            .UseDomainId("domain-id")
            .UseSharedHttpClientPool(false)
            .UseConnectionPool(128, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(3))
            .Build();

        Assert.False(options.UseSharedHttpClientPool);
        Assert.Equal(128, options.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromMinutes(20), options.PooledConnectionLifetime);
        Assert.Equal(TimeSpan.FromMinutes(3), options.PooledConnectionIdleTimeout);
    }
}

public class EntityMetadataJsonConversionTests
{
    [Fact]
    public void ToRowData_Serializes_Complex_Property_As_Json_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var entity = new CartEntity
        {
            Id = Guid.NewGuid(),
            OrderData = new OrderDataEntity { Comment = "hello", DiscountApplied = true }
        };

        var row = metadata.ToRowData(entity);

        var value = Assert.IsType<string>(row["OrderData"]);
        Assert.Contains("\"comment\":\"hello\"", value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discountApplied\":true", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Materialize_Deserializes_Complex_Property_From_Json_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var rowData = new Dictionary<string, object?>
        {
            ["OrderData"] = "{\"comment\":\"server\",\"discountApplied\":false}"
        };

        var entity = (CartEntity)metadata.Materialize(Guid.NewGuid().ToString(), rowData, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(entity.OrderData);
        Assert.Equal("server", entity.OrderData.Comment);
        Assert.False(entity.OrderData.DiscountApplied);
    }

    [Fact]
    public void ToRowData_Serializes_List_And_IList_As_Json_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var entity = new CartEntity
        {
            Id = Guid.NewGuid(),
            ProductNames = new List<string> { "A", "B" },
            Quantities = new List<int> { 1, 2 }
        };

        var row = metadata.ToRowData(entity);

        Assert.Equal("[\"A\",\"B\"]", row["ProductNames"]);
        Assert.Equal("[1,2]", row["Quantities"]);
    }

    [Fact]
    public void ToRowData_Serializes_Product_List_As_Json_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var entity = new CartEntity
        {
            Id = Guid.NewGuid(),
            Products =
            [
                new CartProduct { ProductId = Guid.NewGuid(), Quantity = 2 },
                new CartProduct { ProductId = Guid.NewGuid(), Quantity = 1 }
            ]
        };

        var row = metadata.ToRowData(entity);

        var json = Assert.IsType<string>(row["Products"]);
        Assert.Contains("productId", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quantity", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Materialize_Deserializes_List_And_IList_From_JsonElement_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var namesDoc = JsonDocument.Parse("\"[\\\"A\\\",\\\"B\\\"]\"");
        using var qtyDoc = JsonDocument.Parse("\"[1,2]\"");

        var rowData = new Dictionary<string, object?>
        {
            ["ProductNames"] = namesDoc.RootElement.Clone(),
            ["Quantities"] = qtyDoc.RootElement.Clone()
        };

        var entity = (CartEntity)metadata.Materialize(Guid.NewGuid().ToString(), rowData, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(["A", "B"], entity.ProductNames);
        Assert.NotNull(entity.Quantities);
        Assert.Equal([1, 2], entity.Quantities!.ToList());
    }

    [Fact]
    public void Materialize_Deserializes_Product_List_From_Json_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var rowData = new Dictionary<string, object?>
        {
            ["Products"] = $"[{{\"productId\":\"{id1}\",\"quantity\":2}},{{\"productId\":\"{id2}\",\"quantity\":1}}]"
        };

        var entity = (CartEntity)metadata.Materialize(Guid.NewGuid().ToString(), rowData, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(entity.Products);
        Assert.Equal(2, entity.Products!.Count);
        Assert.Equal(id1, entity.Products[0].ProductId);
        Assert.Equal(2, entity.Products[0].Quantity);
        Assert.Equal(id2, entity.Products[1].ProductId);
        Assert.Equal(1, entity.Products[1].Quantity);
    }

    [Fact]
    public void ToRowData_With_SourceGen_Options_Serializes_Custom_List()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new StringOnlyResolver()
        };

        var metadata = EntityMetadata.Build(typeof(SourceGenEntity), new Dictionary<Type, EntityTypeMapBuilder>(), options);
        var entity = new SourceGenEntity
        {
            Id = Guid.NewGuid(),
            Metadata = [new SourceGenMetadata { Name = "tier", Value = "gold" }]
        };

        var row = metadata.ToRowData(entity);

        var json = Assert.IsType<string>(row["Metadata"]);
        Assert.Contains("\"name\":\"tier\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"value\":\"gold\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Materialize_With_SourceGen_Options_Deserializes_Custom_List()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new StringOnlyResolver()
        };

        var metadata = EntityMetadata.Build(typeof(SourceGenEntity), new Dictionary<Type, EntityTypeMapBuilder>(), options);
        var rowData = new Dictionary<string, object?>
        {
            ["Metadata"] = "[{\"name\":\"priority\",\"value\":\"high\"}]"
        };

        var entity = (SourceGenEntity)metadata.Materialize(Guid.NewGuid().ToString(), rowData, options);

        Assert.Single(entity.Metadata);
        Assert.Equal("priority", entity.Metadata[0].Name);
        Assert.Equal("high", entity.Metadata[0].Value);
    }

    [Fact]
    public void ToRowData_Serializes_Enum_As_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var entity = new CartEntity
        {
            Id = Guid.NewGuid(),
            State = CartState.CheckedOut
        };

        var row = metadata.ToRowData(entity);

        Assert.Equal("CheckedOut", row["State"]);
    }

    [Fact]
    public void Materialize_Deserializes_Enum_From_JsonElement_String()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var stateDoc = JsonDocument.Parse("\"CheckedOut\"");
        var rowData = new Dictionary<string, object?>
        {
            ["State"] = stateDoc.RootElement.Clone()
        };

        var entity = (CartEntity)metadata.Materialize(Guid.NewGuid().ToString(), rowData, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(CartState.CheckedOut, entity.State);
    }

    [Fact]
    public void Guid_Property_Defaults_To_Text_ValueType()
    {
        var metadata = EntityMetadata.Build(typeof(CartEntity), new Dictionary<Type, EntityTypeMapBuilder>(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var column = Assert.Single(metadata.ExpectedColumns.Where(col => col.Name == "CompanyId"));
        Assert.Equal("text", column.ValueTypeKey);
    }

    [UserTable("CARTS")]
    private sealed class CartEntity
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public OrderDataEntity? OrderData { get; set; }
        public List<string>? ProductNames { get; set; }
        public List<CartProduct>? Products { get; set; }
        public IList<int>? Quantities { get; set; }
        public CartState State { get; set; }
    }

    private sealed class CartProduct
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
    }

    private enum CartState
    {
        Open,
        CheckedOut
    }

    private sealed class OrderDataEntity
    {
        public string? Comment { get; set; }
        public bool DiscountApplied { get; set; }
    }

    [UserTable("SOURCEGEN_TEST")]
    private sealed class SourceGenEntity
    {
        public Guid Id { get; set; }
        public List<SourceGenMetadata> Metadata { get; set; } = [];
    }

    private sealed class SourceGenMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class StringOnlyResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver _fallback = new DefaultJsonTypeInfoResolver();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            return type == typeof(string)
                ? _fallback.GetTypeInfo(type, options)
                : null;
        }
    }
}

public class LocalhostIntegrationTests
{
    [Fact]
    public async Task Localhost_Roundtrip_Works_For_Nested_List_And_Enum()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("UT_LOCALHOST_RUN"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var baseUrl = Environment.GetEnvironmentVariable("UT_BASE_URL") ?? throw new InvalidOperationException("UT_BASE_URL is required.");
        var apiKey = Environment.GetEnvironmentVariable("UT_API_KEY") ?? throw new InvalidOperationException("UT_API_KEY is required.");
        var apiSecret = Environment.GetEnvironmentVariable("UT_API_SECRET") ?? throw new InvalidOperationException("UT_API_SECRET is required.");
        var domainId = Environment.GetEnvironmentVariable("UT_DOMAIN_ID") ?? throw new InvalidOperationException("UT_DOMAIN_ID is required.");

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var httpClient = new HttpClient(handler);

        var tableName = $"dotnet_it_carts_{Guid.NewGuid():N}";
        var productsTableName = $"dotnet_it_products_{Guid.NewGuid():N}";
        var options = new UserTablesContextOptionsBuilder()
            .UseBaseUrl(baseUrl)
            .UseApiKey(apiKey)
            .UseBearerToken(apiSecret)
            .UseDomainId(domainId)
            .UseHttpClient(httpClient)
            .AllowAutoMigrationDanger(true)
            .AutoMigrationAddOnly(true)
            .Build();

        await using var db = new IntegrationCartContext(options, tableName, productsTableName);

        var entity = new IntegrationCartEntity
        {
            OrderData = new IntegrationOrderData { Comment = "integration", DiscountApplied = true },
            ProductNames = ["A", "B"],
            Products =
            [
                new IntegrationCartProduct { ProductId = Guid.NewGuid(), Quantity = 2 },
                new IntegrationCartProduct { ProductId = Guid.NewGuid(), Quantity = 1 }
            ],
            State = IntegrationCartState.CheckedOut
        };

        await db.Carts.AddAsync(entity);
        await db.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(entity.Id));

        var loaded = await db.Carts.FindAsync(entity.Id!);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.OrderData);
        Assert.Equal("integration", loaded.OrderData!.Comment);
        Assert.True(loaded.OrderData.DiscountApplied);
        Assert.Equal(["A", "B"], loaded.ProductNames);
        Assert.NotNull(loaded.Products);
        Assert.Equal(2, loaded.Products!.Count);
        Assert.Equal(entity.Products![0].ProductId, loaded.Products[0].ProductId);
        Assert.Equal(entity.Products[0].Quantity, loaded.Products[0].Quantity);
        Assert.Equal(entity.Products[1].ProductId, loaded.Products[1].ProductId);
        Assert.Equal(entity.Products[1].Quantity, loaded.Products[1].Quantity);
        Assert.Equal(IntegrationCartState.CheckedOut, loaded.State);

        db.Carts.Remove(loaded);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Localhost_Can_Add_Actual_Product_Rows()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("UT_LOCALHOST_RUN"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var baseUrl = Environment.GetEnvironmentVariable("UT_BASE_URL") ?? throw new InvalidOperationException("UT_BASE_URL is required.");
        var apiKey = Environment.GetEnvironmentVariable("UT_API_KEY") ?? throw new InvalidOperationException("UT_API_KEY is required.");
        var apiSecret = Environment.GetEnvironmentVariable("UT_API_SECRET") ?? throw new InvalidOperationException("UT_API_SECRET is required.");
        var domainId = Environment.GetEnvironmentVariable("UT_DOMAIN_ID") ?? throw new InvalidOperationException("UT_DOMAIN_ID is required.");

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var httpClient = new HttpClient(handler);

        var cartsTableName = $"dotnet_it_carts_{Guid.NewGuid():N}";
        var productsTableName = $"dotnet_it_products_{Guid.NewGuid():N}";
        var marker = $"it_prod_{Guid.NewGuid():N}";

        var options = new UserTablesContextOptionsBuilder()
            .UseBaseUrl(baseUrl)
            .UseApiKey(apiKey)
            .UseBearerToken(apiSecret)
            .UseDomainId(domainId)
            .UseHttpClient(httpClient)
            .AllowAutoMigrationDanger(true)
            .AutoMigrationAddOnly(true)
            .Build();

        await using var db = new IntegrationCartContext(options, cartsTableName, productsTableName);

        var p1 = new IntegrationProductEntity
        {
            Name = $"{marker}_A",
            Price = 10.5m
        };
        var p2 = new IntegrationProductEntity
        {
            Name = $"{marker}_B",
            Price = 25m
        };

        await db.Products.AddAsync(p1);
        await db.Products.AddAsync(p2);
        await db.SaveChangesAsync();

        Assert.False(string.IsNullOrWhiteSpace(p1.Id));
        Assert.False(string.IsNullOrWhiteSpace(p2.Id));

        var row1 = await db.Products.FindAsync(p1.Id!);
        var row2 = await db.Products.FindAsync(p2.Id!);
        Assert.NotNull(row1);
        Assert.NotNull(row2);
        Assert.Equal(p1.Name, row1!.Name);
        Assert.Equal(p1.Price, row1.Price);
        Assert.Equal(p2.Name, row2!.Name);
        Assert.Equal(p2.Price, row2.Price);

        db.Products.Remove(row1);
        db.Products.Remove(row2);
        await db.SaveChangesAsync();
    }

    private sealed class IntegrationCartContext(UserTablesContextOptions options, string tableName, string productsTableName) : UserTablesDbContext(options)
    {
        private readonly string _tableName = tableName;
        private readonly string _productsTableName = productsTableName;

        public UserTableSet<IntegrationCartEntity> Carts => Set<IntegrationCartEntity>();
        public UserTableSet<IntegrationProductEntity> Products => Set<IntegrationProductEntity>();

        protected override void OnModelCreating(UserTablesModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IntegrationCartEntity>()
                .ToTable(_tableName)
                .HasKey(x => x.Id);

            modelBuilder.Entity<IntegrationProductEntity>()
                .ToTable(_productsTableName)
                .HasKey(x => x.Id);
        }
    }

    private sealed class IntegrationCartEntity
    {
        [UserTableKey]
        public string? Id { get; set; }
        public IntegrationOrderData? OrderData { get; set; }
        public List<string>? ProductNames { get; set; }
        public List<IntegrationCartProduct>? Products { get; set; }
        public IntegrationCartState State { get; set; }
    }

    private sealed class IntegrationCartProduct
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
    }

    private sealed class IntegrationProductEntity
    {
        [UserTableKey]
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    private sealed class IntegrationOrderData
    {
        public string? Comment { get; set; }
        public bool DiscountApplied { get; set; }
    }

    private enum IntegrationCartState
    {
        Open,
        CheckedOut
    }
}

public class TransportJsonContextTests
{
    [Fact]
    public void RowDataPayload_Serializes_DateTime_And_DateTimeOffset()
    {
        var payload = new RowDataPayload
        {
            Data = new Dictionary<string, object?>
            {
                ["CreatedAt"] = new DateTime(2026, 2, 23, 12, 34, 56, DateTimeKind.Utc),
                ["UpdatedAt"] = new DateTimeOffset(2026, 2, 23, 12, 34, 56, TimeSpan.Zero)
            }
        };

        var context = new UserTablesTransportJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, context.RowDataPayload);
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"CreatedAt\":\"", json, StringComparison.Ordinal);
        Assert.Contains("\"UpdatedAt\":\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RowDataPayload_Serializes_Numeric_And_Scalar_Runtime_Types()
    {
        var payload = new RowDataPayload
        {
            Data = new Dictionary<string, object?>
            {
                ["CharValue"] = 'Z',
                ["BoolValue"] = true,
                ["ByteValue"] = (byte)7,
                ["SByteValue"] = (sbyte)-7,
                ["ShortValue"] = (short)-123,
                ["UShortValue"] = (ushort)123,
                ["IntValue"] = 12345,
                ["UIntValue"] = (uint)12345,
                ["LongValue"] = 1234567890123L,
                ["ULongValue"] = 1234567890123UL,
                ["FloatValue"] = 1.5f,
                ["DoubleValue"] = 2.5d,
                ["DecimalValue"] = 3.5m,
                ["GuidValue"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ["TimeSpanValue"] = TimeSpan.FromMinutes(5)
            }
        };

        var context = new UserTablesTransportJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, context.RowDataPayload);
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"CharValue\":\"Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"BoolValue\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"ByteValue\":7", json, StringComparison.Ordinal);
        Assert.Contains("\"SByteValue\":-7", json, StringComparison.Ordinal);
        Assert.Contains("\"ShortValue\":-123", json, StringComparison.Ordinal);
        Assert.Contains("\"UShortValue\":123", json, StringComparison.Ordinal);
        Assert.Contains("\"IntValue\":12345", json, StringComparison.Ordinal);
        Assert.Contains("\"UIntValue\":12345", json, StringComparison.Ordinal);
        Assert.Contains("\"LongValue\":1234567890123", json, StringComparison.Ordinal);
        Assert.Contains("\"ULongValue\":1234567890123", json, StringComparison.Ordinal);
        Assert.Contains("\"FloatValue\":1.5", json, StringComparison.Ordinal);
        Assert.Contains("\"DoubleValue\":2.5", json, StringComparison.Ordinal);
        Assert.Contains("\"DecimalValue\":3.5", json, StringComparison.Ordinal);
        Assert.Contains("\"GuidValue\":\"11111111-1111-1111-1111-111111111111\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TimeSpanValue\":\"00:05:00\"", json, StringComparison.Ordinal);
    }
}
