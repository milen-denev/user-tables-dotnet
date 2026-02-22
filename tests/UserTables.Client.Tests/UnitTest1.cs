using UserTables.Client.Query;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using UserTables.Client.Attributes;
using UserTables.Client.Configuration;
using UserTables.Client.Internal;

namespace UserTables.Client.Tests;

public class PredicateTranslatorTests
{
    [Fact]
    public void Translates_Equality_Expression_To_Server_Filter()
    {
        Expression<Func<LeadEntity, bool>> predicate = lead => lead.Name == "Acme";

        var translated = PredicateTranslator.TryTranslateFilter(predicate);

        Assert.NotNull(translated);
        Assert.Equal("Name", translated?.Column);
        Assert.Equal("Acme", translated?.Value);
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

    [UserTable("CARTS")]
    private sealed class CartEntity
    {
        public Guid Id { get; set; }
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
