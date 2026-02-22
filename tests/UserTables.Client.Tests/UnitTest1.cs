using UserTables.Client.Query;
using System.Collections.Generic;
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

    [UserTable("CARTS")]
    private sealed class CartEntity
    {
        public Guid Id { get; set; }
        public OrderDataEntity? OrderData { get; set; }
    }

    private sealed class OrderDataEntity
    {
        public string? Comment { get; set; }
        public bool DiscountApplied { get; set; }
    }
}
