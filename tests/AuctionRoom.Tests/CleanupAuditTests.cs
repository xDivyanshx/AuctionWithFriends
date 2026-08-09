using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace AuctionRoom.Tests;

/// <summary>
/// Housekeeping check: <see cref="SwapScenario"/> tears down everything it creates,
/// so a completed test run must leave no rows tagged with the "test" season behind.
/// Run after the suite to confirm the shared Neon database stayed clean.
/// </summary>
[Collection("Neon")]
public class CleanupAuditTests
{
    private readonly ITestOutputHelper _out;
    public CleanupAuditTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task NoTestScaffoldingLeftBehindInNeon()
    {
        await using var db = SwapScenario.NewDb();

        var testRooms = await db.Rooms.CountAsync(r => r.Season == "test");
        var testPools = await db.PlayerPools.CountAsync(p => p.Season == "test");
        var testPlayers = await db.Players.CountAsync(p => p.ExternalId!.StartsWith("test-"));
        var testUsers = await db.Users.CountAsync(u => u.Name.StartsWith("Alice-") || u.Name.StartsWith("Bob-"));

        var realRooms = await db.Rooms.CountAsync(r => r.Season != "test");
        var realHoldings = await db.Holdings.CountAsync();
        var realPlayers = await db.Players.CountAsync(p => !p.ExternalId!.StartsWith("test-"));
        var swaps = await db.Swaps.CountAsync();

        _out.WriteLine($"leftover test rooms   : {testRooms}");
        _out.WriteLine($"leftover test pools   : {testPools}");
        _out.WriteLine($"leftover test players : {testPlayers}");
        _out.WriteLine($"leftover test users   : {testUsers}");
        _out.WriteLine($"--- real data ---");
        _out.WriteLine($"rooms                 : {realRooms}");
        _out.WriteLine($"holdings              : {realHoldings}");
        _out.WriteLine($"pool players          : {realPlayers}");
        _out.WriteLine($"swaps                 : {swaps}");

        Assert.Equal(0, testRooms);
        Assert.Equal(0, testPools);
        Assert.Equal(0, testPlayers);
        Assert.Equal(0, testUsers);
    }
}
