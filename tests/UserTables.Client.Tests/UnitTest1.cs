using UserTables.Client.Query;

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
