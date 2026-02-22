using UserTables.Client;
using UserTables.Client.Attributes;
using UserTables.Client.Configuration;

var options = new UserTablesContextOptionsBuilder()
	.UseBaseUrl("https://vynflow.cloud")
	.UseApiKey("sa-k-vynflow-KEY")
	.UseBearerToken("sa-p-vynflow-SECRET")
	.UseDomainId("DOMAIN_UUID")
	.AllowAutoMigrationDanger(true)
	.AutoMigrationAddOnly(true)
	.UseRetryPolicy(3, TimeSpan.FromMilliseconds(200))
	.Build();

await using var db = new MarketplaceContext(options);

var leads = await db.Leads
	.WhereEq("Active", true)
	.WhereStartsWith("Email", "sales@")
	.UseAndFilters()
	.OrderByDescending(x => x.Email)
	.Take(10)
	.AsNoTracking()
	.ToListAsync();

Console.WriteLine($"Fetched {leads.Count} lead(s)");

var newLead = new Lead
{
	Name = "Contoso",
	Email = "sales@contoso.com",
	Active = true
};

await db.Leads.AddAsync(newLead);
await db.SaveChangesAsync();

newLead.Active = false;
await db.SaveChangesAsync();

db.Leads.Remove(newLead);
await db.SaveChangesAsync();

public sealed class MarketplaceContext(UserTablesContextOptions options) : UserTablesDbContext(options)
{
	public UserTableSet<Lead> Leads => Set<Lead>();

	protected override void OnModelCreating(UserTablesModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Lead>()
			.ToTable("TABLE_UUID")
			.HasKey(x => x.Id)
			.Property(x => x.Email)
			.HasColumnName("Email");
	}
}

[UserTable("TABLE_UUID")]
public sealed class Lead
{
	[UserTableKey]
	public string? Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Email { get; set; } = string.Empty;
	public bool Active { get; set; }
}
