using AuctionRoom.Api.Services;
using AuctionRoom.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuctionRoom.Tests;

/// <summary>
/// The other half of Phase 3: every operation now names the status it belongs
/// to, so a room that is in the wrong one refuses rather than quietly working.
/// Each of these was possible before this phase — recording created the auction,
/// swaps were legal during it, undo had no guard at all, and joining a running
/// auction was fine.
/// </summary>
[Collection("Neon")]
public class LifecycleGateTests
{
    private static AuctionService Auction(SwapScenario s) => new(s.Db, new ShortlistService(s.Db));
    private static SwapService Swaps(SwapScenario s) => new(s.Db, new FakeClock(FakeClock.SaturdayIst));
    private static RoomService Rooms(SwapScenario s) => new(s.Db);

    private static async Task SetStatusAsync(SwapScenario s, RoomStatus status)
    {
        s.Room.Status = status;
        await s.Db.SaveChangesAsync();
    }

    // --- Recording ---

    [Fact]
    public async Task Record_RejectedInSetup()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Auction(s).RecordResultAsync(s.Room.Id, s.Pool[4].Id, s.Alice.Id, 10));
        Assert.Contains("not started", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The old implicit flip would have left the room in Auction here.
        await using var check = SwapScenario.NewDb();
        Assert.Equal(RoomStatus.Setup,
            (await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).Status);
        Assert.False(await check.AuctionResults.AnyAsync(a => a.RoomId == s.Room.Id));
    }

    [Fact]
    public async Task Record_RejectedInWar()
    {
        await using var s = await SwapScenario.CreateAsync(); // fixture starts in War

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Auction(s).RecordResultAsync(s.Room.Id, s.Pool[4].Id, s.Alice.Id, 10));
        Assert.Contains("swaps", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Record_ClearsTheNominationItSettles()
    {
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Auction;
        s.Room.CurrentNominationPlayerId = s.Pool[4].Id;
        await s.Db.SaveChangesAsync();

        await Auction(s).RecordResultAsync(s.Room.Id, s.Pool[4].Id, s.Alice.Id, 10);

        await using var check = SwapScenario.NewDb();
        Assert.Null((await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).CurrentNominationPlayerId);
    }

    [Fact]
    public async Task Record_LeavesADifferentPlayersNominationStanding()
    {
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Auction;
        s.Room.CurrentNominationPlayerId = s.Pool[4].Id;
        await s.Db.SaveChangesAsync();

        // The host sells P5 from the manual picker while P4 is on the block. P4
        // is still unsold, so the block must not be emptied.
        await Auction(s).RecordResultAsync(s.Room.Id, s.Pool[5].Id, s.Alice.Id, 10);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(s.Pool[4].Id,
            (await check.Rooms.SingleAsync(r => r.Id == s.Room.Id)).CurrentNominationPlayerId);
    }

    // --- Undo ---

    [Fact]
    public async Task Undo_RejectedOnceTheAuctionIsOver()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Auction);

        var sold = await Auction(s).RecordResultAsync(s.Room.Id, s.Pool[4].Id, s.Alice.Id, 10);
        await SetStatusAsync(s, RoomStatus.War);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Auction(s).UndoLastAsync(s.Room.Id));
        Assert.Contains("no longer be undone", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Undo after the auction would refund a buy whose player may since have
        // been swapped away — so the sale and the budget must both survive.
        await using var check = SwapScenario.NewDb();
        Assert.True(await check.AuctionResults.AnyAsync(a => a.Id == sold.Id));
        Assert.Equal(90, (await check.Participants.SingleAsync(p => p.Id == s.Alice.Id)).BudgetRemaining);
    }

    [Fact]
    public async Task Undo_WorksDuringTheAuctionAndFreesThePlayer()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Auction);

        await Auction(s).RecordResultAsync(s.Room.Id, s.Pool[4].Id, s.Alice.Id, 10);
        await Auction(s).UndoLastAsync(s.Room.Id);

        await using var check = SwapScenario.NewDb();
        Assert.False(await check.AuctionResults.AnyAsync(a => a.RoomId == s.Room.Id));
        Assert.False(await check.Holdings.AnyAsync(h => h.RoomId == s.Room.Id && h.PlayerId == s.Pool[4].Id));
        Assert.Equal(100, (await check.Participants.SingleAsync(p => p.Id == s.Alice.Id)).BudgetRemaining);
    }

    // --- Swaps ---

    [Fact]
    public async Task Swap_RejectedDuringTheAuction()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Auction);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Swaps(s).SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10));
        Assert.Contains("after the auction ends", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = SwapScenario.NewDb();
        Assert.False(await check.Swaps.AnyAsync(x => x.RoomId == s.Room.Id));
    }

    [Fact]
    public async Task Swap_RejectedInSetup()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Swaps(s).SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10));
        Assert.Contains("not started", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Joining ---

    [Fact]
    public async Task Join_RejectedOnceTheAuctionHasStarted()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Auction);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Rooms(s).JoinRoomAsync(s.Room.Code, $"Latecomer-{s.Room.Code}", "Too Late FC"));
        Assert.Contains("already started", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(2, await check.Participants.CountAsync(p => p.RoomId == s.Room.Id));
    }

    [Fact]
    public async Task Join_RejectedOnceTheAuctionIsOver()
    {
        await using var s = await SwapScenario.CreateAsync(); // War

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Rooms(s).JoinRoomAsync(s.Room.Code, $"Latecomer-{s.Room.Code}", "Too Late FC"));
        Assert.Contains("no one can join", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Join_RejectsTheEleventhParticipantButNotAReturningTenth()
    {
        await using var s = await SwapScenario.CreateAsync();
        await SetStatusAsync(s, RoomStatus.Setup);

        // Fixture already holds 2; fill to the cap of 10.
        var joiner = Rooms(s);
        var names = new List<string>();
        for (int i = 3; i <= RoomLifecycleService.MaxParticipants; i++)
        {
            var name = $"Filler-{s.Room.Code}-{i}";
            names.Add(name);
            await joiner.JoinRoomAsync(s.Room.Code, name, $"Team {i}");
        }

        try
        {
            await using (var check = SwapScenario.NewDb())
            {
                Assert.Equal(RoomLifecycleService.MaxParticipants,
                    await check.Participants.CountAsync(p => p.RoomId == s.Room.Id));
            }

            // A rejoin is idempotent and must not read as an eleventh person.
            var (_, existing, _) = await joiner.JoinRoomAsync(
                s.Room.Code, names[^1], "Renamed, ignored");
            Assert.NotNull(existing);

            var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
                joiner.JoinRoomAsync(s.Room.Code, $"Eleventh-{s.Room.Code}", "Eleventh FC"));
            Assert.Contains("full", ex.Message, StringComparison.OrdinalIgnoreCase);

            await using (var check = SwapScenario.NewDb())
            {
                Assert.Equal(RoomLifecycleService.MaxParticipants,
                    await check.Participants.CountAsync(p => p.RoomId == s.Room.Id));
            }
        }
        finally
        {
            // The scenario's teardown only knows about Alice and Bob, so the
            // fillers (and the eleventh's user row, if it was created) are this
            // test's to clean up.
            await using var cleanup = SwapScenario.NewDb();
            var stray = names.Append($"Eleventh-{s.Room.Code}").ToList();
            await cleanup.Participants.Where(p => p.RoomId == s.Room.Id
                && p.User != null && stray.Contains(p.User.Name)).ExecuteDeleteAsync();
            await cleanup.Users.Where(u => stray.Contains(u.Name)).ExecuteDeleteAsync();
        }
    }
}
