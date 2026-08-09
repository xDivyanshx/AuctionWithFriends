using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuctionRoom.Infrastructure;

/// <summary>
/// Used only by EF Core CLI tools (migrations) at design time. The real
/// connection string is supplied by the API from configuration at runtime.
/// A placeholder is fine here because migrations only need the provider, not
/// a live connection.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuctionDbContext>
{
    public AuctionDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AUCTIONROOM_DB")
            ?? "Host=localhost;Port=5432;Database=auctionroom;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AuctionDbContext(options);
    }
}
