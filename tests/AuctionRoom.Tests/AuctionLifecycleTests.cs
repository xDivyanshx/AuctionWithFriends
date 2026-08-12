using AuctionRoom.Api.Services;
using AuctionRoom.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuctionRoom.Tests;

/// <summary>
/// Phase 3: the explicit Setup → Auction → War lifecycle, and the gates that
/// hang off it. Before this phase the first recorded sale flipped the status
/// silently, so a freshly created room was already recordable and a room in any
/// status accepted swaps — every test here would have passed vacuously or not
/// compiled.
///
/// <para>
/// Run against real Neon like the swap suite, and for the same reason: the
/// AuctionPass unique index and the nomination FK are Postgres behaviour an
/// in-memory provider does not reproduce.
/// </para>
/// </summary>
[Collection("Neon")]
public class AuctionLifecycleTests
{
    private static RoomLifecycleService Lifecycle(SwapScenario s) => new(s.Db);

    /// <summary>
    /// The shared fixture starts rooms in War (it exists for swaps). Rewinding
    /// here rather than parameterising the fixture keeps the swap tests reading
    /// exactly as they did.
    /// </summary>
    private static async Task SetStatusAsync(SwapScenario s, RoomStatus status)
    {
        s.Room.Status = status;
        await s.Db.SaveChangesAsync();
    }

    // --- Setup → Auction ---

    [Fact]
    public async Task StartAuction_MovesSetupToAuctionAndOpensRoundOne()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        await Lifecycle(s).StartAuctionAsync(s.Room);

        await using var check = SwapScenario.NewDb();
        var room = await check.Rooms.SingleAsync(r => r.Id == s.Room.Id);
        Assert.Equal(RoomStatus.Auction, room.Status);
        Assert.Equal(1, room.AuctionRound);
        Assert.Null(room.CurrentNominationPlayerId);

        Assert.True(await check.AuditEvents.AnyAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "auction_started"));
    }

    [Fact]
    public async Task StartAuction_RejectedTwice()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        await Lifecycle(s).StartAuctionAsync(s.Room);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Lifecycle(s).StartAuctionAsync(s.Room));
        Assert.Contains("already started", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAuction_RejectedFromWar()
    {
        await using var s = await SwapScenario.CreateAsync(); // fixture starts in War

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Lifecycle(s).StartAuctionAsync(s.Room));
        Assert.Contains("setup", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartAuction_RequiresTwoParticipants()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        // Drop Bob's holdings then Bob, leaving the host alone in the room.
        await s.Db.Holdings.Where(h => h.ParticipantId == s.Bob.Id).ExecuteDeleteAsync();
        await s.Db.Participants.Where(p => p.Id == s.Bob.Id).ExecuteDeleteAsync();

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Lifecycle(s).StartAuctionAsync(s.Room));
        Assert.Contains("2 participants", ex.Message);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(RoomStatus.Setup,
            (await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).Status);
    }

    // --- Auction → War, with the under-filled warning ---

    [Fact]
    public async Task EndAuction_WarnsWithUnderFilledSquadsAndChangesNothing()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Auction);

        // Both squads hold 2 of the configured 15.
        var ex = await Assert.ThrowsAsync<UnderFilledSquadException>(() =>
            Lifecycle(s).EndAuctionAsync(s.Room, confirm: false));

        Assert.Equal(2, ex.Warnings.Count);
        Assert.All(ex.Warnings, w =>
        {
            Assert.Equal(2, w.SquadCount);
            Assert.Equal(15, w.Required);
        });
        // Ordered by team name so the confirm step reads consistently.
        Assert.Equal(
            ex.Warnings.Select(w => w.TeamName).OrderBy(n => n, StringComparer.Ordinal),
            ex.Warnings.Select(w => w.TeamName));

        // A warning is not a transition.
        await using var check = SwapScenario.NewDb();
        Assert.Equal(RoomStatus.Auction,
            (await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).Status);
        Assert.False(await check.AuditEvents.AnyAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "auction_ended"));
    }

    [Fact]
    public async Task EndAuction_ProceedsWhenTheHostConfirms()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Auction);

        await Lifecycle(s).EndAuctionAsync(s.Room, confirm: true);

        await using var check = SwapScenario.NewDb();
        var room = await check.Rooms.SingleAsync(r => r.Id == s.Room.Id);
        Assert.Equal(RoomStatus.War, room.Status);
        Assert.Null(room.CurrentNominationPlayerId);
        Assert.True(await check.AuditEvents.AnyAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "auction_ended"));
    }

    [Fact]
    public async Task EndAuction_NeedsNoConfirmationWhenEverySquadIsFull()
    {
        // Squad size 2 makes the fixture's two-player squads exactly full.
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Auction;
        s.Room.Config = new RoomConfig { Budget = 100, SquadSize = 2, BestN = 2 };
        await s.Db.SaveChangesAsync();

        await Lifecycle(s).EndAuctionAsync(s.Room, confirm: false);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(RoomStatus.War,
            (await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).Status);
    }

    [Fact]
    public async Task EndAuction_RejectedWhenTheAuctionIsNotRunning()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Lifecycle(s).EndAuctionAsync(s.Room, confirm: true));
    }

    [Fact]
    public async Task EndAuction_ClearsAnOpenNomination()
    {
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Auction;
        s.Room.CurrentNominationPlayerId = s.Pool[4].Id;
        await s.Db.SaveChangesAsync();

        await Lifecycle(s).EndAuctionAsync(s.Room, confirm: true);

        await using var check = SwapScenario.NewDb();
        Assert.Null((await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).CurrentNominationPlayerId);
    }

    // --- Round 1 → 2 ---

    [Fact]
    public async Task NextRound_AdvancesToTwoAndClearsTheBlock()
    {
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Auction;
        s.Room.AuctionRound = 1;
        s.Room.CurrentNominationPlayerId = s.Pool[4].Id;
        await s.Db.SaveChangesAsync();

        await Lifecycle(s).NextRoundAsync(s.Room);

        await using var check = SwapScenario.NewDb();
        var room = await check.Rooms.SingleAsync(r => r.Id == s.Room.Id);
        Assert.Equal(2, room.AuctionRound);
        // A nomination belongs to the round it was drawn in: carrying it across
        // would file its pass against a round the player was never drawn in.
        Assert.Null(room.CurrentNominationPlayerId);
        Assert.True(await check.AuditEvents.AnyAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "auction_round_advanced"));
    }

    [Fact]
    public async Task NextRound_RejectedOnceTheUnsoldRoundIsRunning()
    {
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Auction;
        s.Room.AuctionRound = 2;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Lifecycle(s).NextRoundAsync(s.Room));
        Assert.Contains("unsold round", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NextRound_RejectedOutsideTheAuction()
    {
        await using var s = await SwapScenario.CreateAsync(); // War
        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Lifecycle(s).NextRoundAsync(s.Room));
    }
}
