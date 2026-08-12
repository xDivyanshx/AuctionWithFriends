using AuctionRoom.Api.Services;
using AuctionRoom.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuctionRoom.Tests;

/// <summary>
/// The weighted draw and the nomination it produces. The pure weighting maths is
/// exercised without a database — <see cref="NominationService.PickWeighted"/> is
/// static for exactly that reason — and the candidate set, idempotency and pass
/// behaviour against real Neon, where the AuctionPass unique index lives.
/// </summary>
[Collection("Neon")]
public class NominationServiceTests
{
    /// <summary>
    /// A fixed seed, mirroring <see cref="FakeClock"/>: a draw that cannot be
    /// reproduced cannot be asserted on. The service takes its Random for this.
    /// </summary>
    private const int Seed = 20260811;

    private static NominationService Service(SwapScenario s, int seed = Seed) =>
        new(s.Db, new Random(seed));

    private static async Task StartAuctionAsync(SwapScenario s, int round = 1)
    {
        s.Room.Status = RoomStatus.Auction;
        s.Room.AuctionRound = round;
        await s.Db.SaveChangesAsync();
    }

    /// <summary>The fixture's players carry no price, so give them one.</summary>
    private static async Task PriceAsync(SwapScenario s, params int[] nowCosts)
    {
        for (int i = 0; i < nowCosts.Length; i++)
        {
            var p = await s.Db.Players.SingleAsync(x => x.Id == s.Pool[i].Id);
            p.NowCost = nowCosts[i];
        }
        await s.Db.SaveChangesAsync();
    }

    // --- The weighting itself (no database) ---

    [Fact]
    public void PickWeighted_ReturnsNullOnAnEmptyPool()
    {
        Assert.Null(NominationService.PickWeighted([], 6, new Random(Seed)));
    }

    [Fact]
    public void PickWeighted_FavoursExpensivePlayers()
    {
        var cheap = Guid.NewGuid();
        var dear = Guid.NewGuid();
        var candidates = new[]
        {
            new NominationCandidate(cheap, 40),
            new NominationCandidate(dear, 150)
        };

        var rng = new Random(Seed);
        var dearCount = 0;
        for (int i = 0; i < 10_000; i++)
            if (NominationService.PickWeighted(candidates, 6, rng) == dear) dearCount++;

        // (150/40)^6 ≈ 2745, so the expensive one should come up all but always.
        // Asserting a band rather than a point value: this is a probability, and
        // a test that pins it exactly would be asserting the seed, not the design.
        Assert.InRange(dearCount, 9_900, 10_000);
    }

    [Fact]
    public void PickWeighted_AtZeroPowerIsUniform()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var candidates = ids
            .Select((id, i) => new NominationCandidate(id, 40 + i * 40))
            .ToArray();

        var rng = new Random(Seed);
        var counts = ids.ToDictionary(id => id, _ => 0);
        for (int i = 0; i < 20_000; i++)
            counts[NominationService.PickWeighted(candidates, 0, rng)!.Value]++;

        // Price is ignored entirely at k=0, so all four sit near 5,000.
        Assert.All(counts.Values, c => Assert.InRange(c, 4_500, 5_500));
    }

    [Fact]
    public void PickWeighted_NeverStarvesAZeroPricedPlayer()
    {
        // Max(1, cost) keeps every weight positive: a player FPL gave no price
        // must stay drawable rather than making the total undefined.
        var free = Guid.NewGuid();
        var candidates = new[]
        {
            new NominationCandidate(free, 0),
            new NominationCandidate(Guid.NewGuid(), 40)
        };

        var rng = new Random(Seed);
        var drawn = false;
        for (int i = 0; i < 500_000 && !drawn; i++)
            drawn = NominationService.PickWeighted(candidates, 1, rng) == free;

        Assert.True(drawn);
    }

    [Fact]
    public void PickWeighted_SurvivesADegenerateExponent()
    {
        var candidates = new[]
        {
            new NominationCandidate(Guid.NewGuid(), 40),
            new NominationCandidate(Guid.NewGuid(), 150)
        };

        // Each of these would break a naive Math.Pow: negative inverts the whole
        // design, huge overflows to infinity, NaN poisons every weight. None may
        // take the auction down.
        foreach (var power in new[] { -5d, 1e9, double.NaN, double.PositiveInfinity })
            Assert.NotNull(NominationService.PickWeighted(candidates, power, new Random(Seed)));
    }

    [Fact]
    public void PickWeighted_IsReproducibleForASeed()
    {
        var candidates = Enumerable.Range(0, 20)
            .Select(i => new NominationCandidate(Guid.NewGuid(), 40 + i * 6))
            .ToArray();

        var first = Enumerable.Range(0, 50)
            .Select(_ => NominationService.PickWeighted(candidates, 6, new Random(Seed)))
            .ToList();
        var rerun = Enumerable.Range(0, 50)
            .Select(_ => NominationService.PickWeighted(candidates, 6, new Random(Seed)))
            .ToList();

        Assert.Equal(first, rerun);
    }

    // --- The candidate set ---

    [Fact]
    public async Task Candidates_ExcludeOwnedPlayers()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        var candidates = await Service(s).GetCandidatesAsync(s.Room);
        var ids = candidates.Select(c => c.PlayerId).ToList();

        // P0–P3 are held by Alice and Bob; only P4 and P5 are free.
        Assert.Equal(2, ids.Count);
        Assert.Contains(s.Pool[4].Id, ids);
        Assert.Contains(s.Pool[5].Id, ids);
    }

    [Fact]
    public async Task Candidates_HonourTheShortlistWhenTheRoomIsCurated()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        s.Db.ShortlistEntries.Add(new ShortlistEntry { RoomId = s.Room.Id, PlayerId = s.Pool[5].Id });
        await s.Db.SaveChangesAsync();

        var candidates = await Service(s).GetCandidatesAsync(s.Room);
        Assert.Single(candidates);
        Assert.Equal(s.Pool[5].Id, candidates[0].PlayerId);
    }

    [Fact]
    public async Task Candidates_ExcludePlayersPassedThisRoundOnly()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        s.Db.AuctionPasses.Add(new AuctionPass
        {
            RoomId = s.Room.Id,
            PlayerId = s.Pool[4].Id,
            Round = 1
        });
        await s.Db.SaveChangesAsync();

        var round1 = await Service(s).GetCandidatesAsync(s.Room);
        Assert.Equal([s.Pool[5].Id], round1.Select(c => c.PlayerId).ToList());

        // Round 2 re-opens everything unsold without deleting a single pass row.
        s.Room.AuctionRound = 2;
        await s.Db.SaveChangesAsync();

        var round2 = await Service(s).GetCandidatesAsync(s.Room);
        Assert.Equal(2, round2.Count);
        Assert.Contains(round2, c => c.PlayerId == s.Pool[4].Id);
    }

    // --- Nominating ---

    [Fact]
    public async Task Nominate_PutsAPlayerOnTheBlockAndAudits()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);
        await PriceAsync(s, 40, 40, 40, 40, 150, 40);

        var player = await Service(s).NominateAsync(s.Room);

        Assert.NotNull(player);
        await using var check = SwapScenario.NewDb();
        var room = await check.Rooms.SingleAsync(r => r.Id == s.Room.Id);
        Assert.Equal(player!.Id, room.CurrentNominationPlayerId);
        Assert.True(await check.AuditEvents.AnyAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "player_nominated"));
    }

    [Fact]
    public async Task Nominate_IsIdempotentSoAPollCannotReRoll()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        var svc = Service(s);
        var first = await svc.NominateAsync(s.Room);
        var second = await svc.NominateAsync(s.Room);
        var third = await svc.NominateAsync(s.Room);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(first.Id, third!.Id);

        // One draw, one audit row: the repeats were not draws at all.
        await using var check = SwapScenario.NewDb();
        Assert.Equal(1, await check.AuditEvents.CountAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "player_nominated"));
    }

    [Fact]
    public async Task Nominate_ReturnsNullWhenTheRoundIsExhausted()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        // Pass on both free players, leaving nothing to draw.
        foreach (var i in new[] { 4, 5 })
        {
            s.Db.AuctionPasses.Add(new AuctionPass
            {
                RoomId = s.Room.Id,
                PlayerId = s.Pool[i].Id,
                Round = 1
            });
        }
        await s.Db.SaveChangesAsync();

        Assert.Null(await Service(s).NominateAsync(s.Room));
    }

    [Fact]
    public async Task Nominate_RejectedOutsideTheAuction()
    {
        await using var s = await SwapScenario.CreateAsync(); // War
        await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s).NominateAsync(s.Room));
    }

    [Fact]
    public async Task Nominate_RedrawsWhenTheOpenNomineeWasSoldBehindItsBack()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        // A stale nomination should be impossible — record and pass both clear it
        // — but if one ever survives, it must not strand a sold player on the
        // block forever.
        s.Room.CurrentNominationPlayerId = s.Pool[0].Id; // held by Alice
        await s.Db.SaveChangesAsync();

        var drawn = await Service(s).NominateAsync(s.Room);

        Assert.NotNull(drawn);
        Assert.NotEqual(s.Pool[0].Id, drawn!.Id);
    }

    // --- Passing ---

    [Fact]
    public async Task Pass_RecordsTheRoundClearsTheBlockAndAudits()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        var svc = Service(s);
        var nominated = await svc.NominateAsync(s.Room);
        var passed = await svc.PassAsync(s.Room);

        Assert.Equal(nominated!.Id, passed);

        await using var check = SwapScenario.NewDb();
        var room = await check.Rooms.SingleAsync(r => r.Id == s.Room.Id);
        Assert.Null(room.CurrentNominationPlayerId);

        var pass = await check.AuctionPasses.SingleAsync(
            p => p.RoomId == s.Room.Id && p.PlayerId == passed);
        Assert.Equal(1, pass.Round);
        Assert.True(await check.AuditEvents.AnyAsync(
            a => a.RoomId == s.Room.Id && a.EventType == "player_passed"));
    }

    [Fact]
    public async Task Pass_RejectedWithAnEmptyBlock()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        var ex = await Assert.ThrowsAsync<AuctionValidationException>(() =>
            Service(s).PassAsync(s.Room));
        Assert.Contains("no player", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pass_DoesNotDuplicateWhenTheSamePlayerIsPassedAgainInARound()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        var svc = Service(s);
        var nominated = await svc.NominateAsync(s.Room);
        await svc.PassAsync(s.Room);

        // Re-open the same player and pass again — the unique index is the real
        // guarantee, but the service must turn this into a no-op rather than a
        // constraint violation surfacing as a 500.
        s.Room.CurrentNominationPlayerId = nominated!.Id;
        await s.Db.SaveChangesAsync();
        await svc.PassAsync(s.Room);

        await using var check = SwapScenario.NewDb();
        Assert.Equal(1, await check.AuctionPasses.CountAsync(
            p => p.RoomId == s.Room.Id && p.PlayerId == nominated.Id && p.Round == 1));
    }

    [Fact]
    public async Task PassedPlayerIds_SpanEveryRound()
    {
        await using var s = await SwapScenario.CreateAsync();
        await StartAuctionAsync(s);

        s.Db.AuctionPasses.AddRange(
            new AuctionPass { RoomId = s.Room.Id, PlayerId = s.Pool[4].Id, Round = 1 },
            new AuctionPass { RoomId = s.Room.Id, PlayerId = s.Pool[4].Id, Round = 2 },
            new AuctionPass { RoomId = s.Room.Id, PlayerId = s.Pool[5].Id, Round = 2 });
        await s.Db.SaveChangesAsync();

        var ids = await Service(s).GetPassedPlayerIdsAsync(s.Room.Id);

        // Distinct: the picker badges a player Unsold once, not once per round.
        Assert.Equal(2, ids.Count);
        Assert.Contains(s.Pool[4].Id, ids);
        Assert.Contains(s.Pool[5].Id, ids);
    }
}
