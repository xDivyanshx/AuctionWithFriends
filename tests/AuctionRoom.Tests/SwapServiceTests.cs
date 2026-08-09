using AuctionRoom.Api.Services;
using AuctionRoom.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuctionRoom.Tests;

/// <summary>
/// Phase 4 verification, run against the real Neon database with a fake clock so
/// the weekend / monthly-cap / cooldown gates can be exercised on any day.
///
/// Scenario fixture (see <see cref="SwapScenario"/>):
///   Alice holds P0 (100 pts, paid 30) and P1 (80 pts, paid 20), budget 100
///   Bob   holds P2 ( 60 pts, paid 25) and P3 (40 pts, paid 15), budget 100
///   P4 (30 pts) and P5 (20 pts) are unowned
/// </summary>
[Collection("Neon")]
public class SwapServiceTests
{
    private static SwapService Service(SwapScenario s, DateTimeOffset now) =>
        new(s.Db, new FakeClock(now));

    // --- Unsold-pool swaps ---

    [Fact]
    public async Task UnsoldSwap_RefundsReleasedPrice_AndChargesAcquirePrice()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        // Alice releases P0 (paid 30) and acquires P4 for 50 → net = 30 − 50 = −20.
        await svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 50);

        await using var check = SwapScenario.NewDb();
        var alice = await check.Participants.SingleAsync(p => p.Id == s.Alice.Id);
        Assert.Equal(80, alice.BudgetRemaining); // 100 − 20

        var swap = await check.Swaps.SingleAsync(x => x.RoomId == s.Room.Id);
        Assert.Equal(30, swap.RefundAmount);
        Assert.Equal(50, swap.AcquirePrice);
        Assert.Equal(-20, swap.NetBudgetChange);
        Assert.Equal(SwapType.UnsoldPool, swap.Type);
    }

    [Fact]
    public async Task UnsoldSwap_TransfersFrozenPointsIntoTheNewSlot()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        // P0 was acquired at 100 pts and still sits at 100 → slot value 0 so far.
        // Bump P0 to 130 so the slot has accrued 30 points of real value.
        var p0 = await s.Db.Players.SingleAsync(p => p.Id == s.Pool[0].Id);
        p0.TotalPoints = 130;
        await s.Db.SaveChangesAsync();

        await svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10);

        await using var check = SwapScenario.NewDb();
        var slot = await check.Holdings
            .Include(h => h.Player)
            .SingleAsync(h => h.RoomId == s.Room.Id && h.PlayerId == s.Pool[4].Id);

        // The 30 accrued points carry forward; P4 starts from its current total.
        Assert.Equal(30, slot.InheritedPoints);
        Assert.Equal(30, slot.AcquisitionPoints); // P4's total at acquisition
        Assert.Equal(AcquisitionSource.Swap, slot.AcquiredVia);
        Assert.Equal(10, slot.AcquisitionPrice);

        // Slot contributes exactly the inherited value until P4 scores again.
        Assert.Equal(30, StandingsService.SlotContribution(slot));

        // P4 scores 5 → slot is now worth 35.
        var p4 = await check.Players.SingleAsync(p => p.Id == s.Pool[4].Id);
        p4.TotalPoints = 35;
        Assert.Equal(35, StandingsService.SlotContribution(slot));
    }

    [Fact]
    public async Task UnsoldSwap_KeepsSquadSizeConstant()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        await svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10);

        await using var check = SwapScenario.NewDb();
        var count = await check.Holdings.CountAsync(h => h.ParticipantId == s.Alice.Id);
        Assert.Equal(2, count); // unchanged: 1-for-1

        // The released player is no longer held by anyone in the room.
        var stillHeld = await check.Holdings.AnyAsync(h => h.RoomId == s.Room.Id && h.PlayerId == s.Pool[0].Id);
        Assert.False(stillHeld);
    }

    [Fact]
    public async Task UnsoldSwap_RejectsWhenBudgetWouldGoNegative()
    {
        await using var s = await SwapScenario.CreateAsync(aliceBudget: 5);
        var svc = Service(s, FakeClock.SaturdayIst);

        // Refund 30 − price 100 = −70, against a budget of 5.
        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 100));
        Assert.Contains("budget", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(5, (await check.Participants.SingleAsync(p => p.Id == s.Alice.Id)).BudgetRemaining);
        Assert.False(await check.Swaps.AnyAsync(x => x.RoomId == s.Room.Id));
    }

    [Fact]
    public async Task UnsoldSwap_RejectsPlayerAlreadyOwned()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        // Alice tries to acquire P2, which Bob already holds.
        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[2].Id, 10));
        Assert.Contains("already owned", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsoldSwap_RejectsWhenReleasingAPlayerNotOwned()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        // Alice tries to release P2 (Bob's player).
        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[2].Id, s.Pool[4].Id, 10));
    }

    // --- Gates ---

    [Fact]
    public async Task Swap_RejectedOnAWeekday()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.FridayIst);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10));
        Assert.Contains("weekend", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Swap_WeekendBoundaryUsesIstNotUtc()
    {
        await using var s = await SwapScenario.CreateAsync();

        // 2026-08-07 21:00 UTC is still Friday in UTC, but 02:30 Saturday in IST.
        // The gate must read IST, so this is allowed.
        var svc = Service(s, new DateTimeOffset(2026, 8, 7, 21, 0, 0, TimeSpan.Zero));
        await svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10);

        await using var check = SwapScenario.NewDb();
        Assert.True(await check.Swaps.AnyAsync(x => x.RoomId == s.Room.Id));
    }

    [Fact]
    public async Task Swap_RejectsSecondSwapInSameCalendarMonth()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        await svc.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10);

        // Same month, next weekend day, different players → still blocked by the cap.
        var later = Service(s, new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(5.5)));
        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            later.SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[1].Id, s.Pool[5].Id, 10));
        Assert.Contains("already used", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Swap_AllowsANewSwapInTheFollowingMonth()
    {
        await using var s = await SwapScenario.CreateAsync();

        await Service(s, FakeClock.SaturdayIst)
            .SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10);

        // Next month, and far enough out that the per-player cooldown has expired
        // for the players involved this time (P1 and P5 were never swapped).
        await Service(s, FakeClock.NextMonthSaturdayIst)
            .SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[1].Id, s.Pool[5].Id, 10);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(2, await check.Swaps.CountAsync(x => x.RoomId == s.Room.Id));
    }

    [Fact]
    public async Task Swap_RejectsPlayerWithinOneMonthCooldown()
    {
        await using var s = await SwapScenario.CreateAsync();

        // Alice swaps P0 out for P4.
        await Service(s, FakeClock.SaturdayIst)
            .SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10);

        // Bob (fresh monthly cap) tries to pick up P0 the same weekend — P0 is on cooldown.
        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s, FakeClock.SaturdayIst)
                .SwapUnsoldAsync(s.Room.Id, s.Bob.Id, s.Pool[2].Id, s.Pool[0].Id, 10));
        Assert.Contains("cooldown", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Swap_RejectedWhenTournamentCompleted()
    {
        await using var s = await SwapScenario.CreateAsync();
        s.Room.Status = RoomStatus.Completed;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s, FakeClock.SaturdayIst)
                .SwapUnsoldAsync(s.Room.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[4].Id, 10));
        Assert.Contains("completed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Participant-to-participant swaps ---

    [Fact]
    public async Task P2PSwap_ExchangesPlayersAndAppliesBothBudgets()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        // Alice gives P0 (she paid 30) and receives P2 for 40 → net −10 → budget 90.
        // Bob gives P2 (he paid 25) and receives P0 for 35 → net −10 → budget 90.
        await svc.SwapP2PAsync(
            s.Room.Id, s.Alice.Id, s.Bob.Id,
            playerOutId: s.Pool[0].Id, playerInId: s.Pool[2].Id,
            priceForInitiator: 40, priceForCounterparty: 35);

        await using var check = SwapScenario.NewDb();
        var alice = await check.Participants.SingleAsync(p => p.Id == s.Alice.Id);
        var bob = await check.Participants.SingleAsync(p => p.Id == s.Bob.Id);
        Assert.Equal(90, alice.BudgetRemaining);
        Assert.Equal(90, bob.BudgetRemaining);

        // Ownership actually crossed over.
        var aliceHasP2 = await check.Holdings.AnyAsync(h => h.ParticipantId == s.Alice.Id && h.PlayerId == s.Pool[2].Id);
        var bobHasP0 = await check.Holdings.AnyAsync(h => h.ParticipantId == s.Bob.Id && h.PlayerId == s.Pool[0].Id);
        Assert.True(aliceHasP2);
        Assert.True(bobHasP0);

        // Squad sizes unchanged.
        Assert.Equal(2, await check.Holdings.CountAsync(h => h.ParticipantId == s.Alice.Id));
        Assert.Equal(2, await check.Holdings.CountAsync(h => h.ParticipantId == s.Bob.Id));
    }

    [Fact]
    public async Task P2PSwap_CountsAgainstBothParticipantsMonthlyCaps()
    {
        await using var s = await SwapScenario.CreateAsync();
        var svc = Service(s, FakeClock.SaturdayIst);

        await svc.SwapP2PAsync(s.Room.Id, s.Alice.Id, s.Bob.Id, s.Pool[0].Id, s.Pool[2].Id, 10, 10);

        await using var check = SwapScenario.NewDb();
        var alice = await check.Participants.SingleAsync(p => p.Id == s.Alice.Id);
        var bob = await check.Participants.SingleAsync(p => p.Id == s.Bob.Id);
        Assert.Equal("2026-08", alice.LastSwapMonth);
        Assert.Equal("2026-08", bob.LastSwapMonth);

        // Bob now cannot do his own swap this month.
        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s, FakeClock.SaturdayIst)
                .SwapUnsoldAsync(s.Room.Id, s.Bob.Id, s.Pool[3].Id, s.Pool[4].Id, 10));
        Assert.Contains("already used", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task P2PSwap_TransfersFrozenPointsOnBothSides()
    {
        await using var s = await SwapScenario.CreateAsync();

        // Give each slot some accrued value: P0 100→120 (+20), P2 60→75 (+15).
        var p0 = await s.Db.Players.SingleAsync(p => p.Id == s.Pool[0].Id);
        var p2 = await s.Db.Players.SingleAsync(p => p.Id == s.Pool[2].Id);
        p0.TotalPoints = 120;
        p2.TotalPoints = 75;
        await s.Db.SaveChangesAsync();

        await Service(s, FakeClock.SaturdayIst).SwapP2PAsync(
            s.Room.Id, s.Alice.Id, s.Bob.Id, s.Pool[0].Id, s.Pool[2].Id, 10, 10);

        await using var check = SwapScenario.NewDb();
        var aliceSlot = await check.Holdings.Include(h => h.Player)
            .SingleAsync(h => h.ParticipantId == s.Alice.Id && h.PlayerId == s.Pool[2].Id);
        var bobSlot = await check.Holdings.Include(h => h.Player)
            .SingleAsync(h => h.ParticipantId == s.Bob.Id && h.PlayerId == s.Pool[0].Id);

        // Each side keeps the value their own released slot had accrued.
        Assert.Equal(20, aliceSlot.InheritedPoints);
        Assert.Equal(75, aliceSlot.AcquisitionPoints);
        Assert.Equal(20, StandingsService.SlotContribution(aliceSlot));

        Assert.Equal(15, bobSlot.InheritedPoints);
        Assert.Equal(120, bobSlot.AcquisitionPoints);
        Assert.Equal(15, StandingsService.SlotContribution(bobSlot));
    }

    [Fact]
    public async Task P2PSwap_RejectsWhenCounterpartyDoesNotOwnIncomingPlayer()
    {
        await using var s = await SwapScenario.CreateAsync();

        // P4 is unowned, so Bob cannot hand it over in a P2P swap.
        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s, FakeClock.SaturdayIst).SwapP2PAsync(
                s.Room.Id, s.Alice.Id, s.Bob.Id, s.Pool[0].Id, s.Pool[4].Id, 10, 10));
    }

    [Fact]
    public async Task P2PSwap_RejectsSelfSwap()
    {
        await using var s = await SwapScenario.CreateAsync();
        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s, FakeClock.SaturdayIst).SwapP2PAsync(
                s.Room.Id, s.Alice.Id, s.Alice.Id, s.Pool[0].Id, s.Pool[1].Id, 10, 10));
    }

    [Fact]
    public async Task P2PSwap_RejectsWhenEitherBudgetWouldGoNegative()
    {
        await using var s = await SwapScenario.CreateAsync(bobBudget: 1);

        // Bob: refund 25 − price 100 = −75 against a budget of 1.
        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s, FakeClock.SaturdayIst).SwapP2PAsync(
                s.Room.Id, s.Alice.Id, s.Bob.Id, s.Pool[0].Id, s.Pool[2].Id, 10, 100));

        // Nothing was applied to either side.
        await using var check = SwapScenario.NewDb();
        Assert.Equal(100, (await check.Participants.SingleAsync(p => p.Id == s.Alice.Id)).BudgetRemaining);
        Assert.Equal(1, (await check.Participants.SingleAsync(p => p.Id == s.Bob.Id)).BudgetRemaining);
        Assert.False(await check.Swaps.AnyAsync(x => x.RoomId == s.Room.Id));
    }
}

/// <summary>
/// These tests share one Neon database, so xUnit must not run them in parallel.
/// </summary>
[CollectionDefinition("Neon", DisableParallelization = true)]
public class NeonCollection { }
