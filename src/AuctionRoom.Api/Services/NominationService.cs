using System.Text.Json;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>One candidate in the draw: everything the weighting needs, nothing else.</summary>
public readonly record struct NominationCandidate(Guid PlayerId, int NowCost);

/// <summary>
/// Decides who goes on the block next. The host no longer picks the order — the
/// server draws a name at random, weighted by FPL price, so expensive players
/// tend to come up first and the money leaves the room early.
///
/// <para>
/// The decline is emergent, not scripted: a premium player has a high chance of
/// being drawn early, so they leave the pool early, so the remaining pool's
/// average price falls on its own. That is why there is no separate "auction
/// progress" term — it would be a second knob doing the same job.
/// </para>
///
/// <para>
/// Price rather than points is deliberate. CLAUDE.md §12 (2026-08-06) records
/// that FPL's pre-season <c>total_points</c> is scrambled carryover from last
/// season, so at auction time price is the only honest signal of who is good.
/// </para>
/// </summary>
public class NominationService
{
    private readonly AuctionDbContext _db;
    private readonly Random _rng;

    /// <summary>
    /// The RNG is injected for the same reason <see cref="SwapService"/> takes a
    /// <see cref="TimeProvider"/>: a draw has to be reproducible in a test. It is
    /// registered as a singleton, so successive draws in one process advance the
    /// same sequence rather than reseeding per request.
    /// </summary>
    public NominationService(AuctionDbContext db, Random rng)
    {
        _db = db;
        _rng = rng;
    }

    /// <summary>
    /// Clamp on <see cref="RoomConfig.NominationPower"/>. A negative exponent
    /// would invert the whole design and favour the cheapest players; a huge one
    /// overflows <c>Math.Pow</c> to infinity and every weight becomes NaN. 20 is
    /// already effectively a strict descending order.
    /// </summary>
    public const double MaxPower = 20;

    /// <summary>
    /// Pick one candidate with probability proportional to <c>NowCost^power</c>.
    /// Pure and static so the tuning harness can sweep thousands of draws over the
    /// real pool without a database or a service instance.
    /// </summary>
    public static Guid? PickWeighted(
        IReadOnlyList<NominationCandidate> candidates,
        double power,
        Random rng)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0].PlayerId;

        var k = double.IsNaN(power) ? 0 : Math.Clamp(power, 0, MaxPower);

        // Max(1, cost) keeps every weight strictly positive: a player with a
        // missing or zero price is still drawable, just at the floor, rather
        // than making the total zero and the roll undefined.
        var weights = new double[candidates.Count];
        var total = 0d;
        for (int i = 0; i < candidates.Count; i++)
        {
            var w = Math.Pow(Math.Max(1, candidates[i].NowCost), k);
            weights[i] = w;
            total += w;
        }

        // Degenerate only if the arithmetic broke (inf/NaN); fall back to uniform
        // so a bad config value cannot take the auction down.
        if (!double.IsFinite(total) || total <= 0)
            return candidates[rng.Next(candidates.Count)].PlayerId;

        var roll = rng.NextDouble() * total;
        var cumulative = 0d;
        for (int i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return candidates[i].PlayerId;
        }

        // Floating-point drift can leave `roll` a hair above the final cumulative
        // sum. Returning the last candidate is correct, not a guess.
        return candidates[^1].PlayerId;
    }

    /// <summary>
    /// Everyone still up for auction in this room's current round: auctionable,
    /// not already owned, not passed over this round.
    /// </summary>
    /// <remarks>
    /// "Auctionable" is the shortlist when the host curated one and the whole
    /// pool when they did not — the same "empty means unrestricted" rule
    /// <see cref="ShortlistService.IsAuctionableAsync"/> applies, expressed here
    /// as a correlated EXISTS so a ~200-entry shortlist is not shipped to the
    /// server as a parameter list on every draw.
    ///
    /// Ownership is read from <c>Holdings</c> rather than <c>AuctionResults</c>
    /// because it is the live-ownership table; during an auction the two agree
    /// exactly (record opens a holding, undo closes it), so undoing a sale puts
    /// the player back in the draw.
    /// </remarks>
    public async Task<List<NominationCandidate>> GetCandidatesAsync(
        Room room,
        CancellationToken ct = default)
    {
        if (room.PlayerPoolId is not Guid poolId)
            return [];

        var players = _db.Players.Where(p => p.PoolId == poolId);

        var curated = await _db.ShortlistEntries.AnyAsync(s => s.RoomId == room.Id, ct);
        if (curated)
        {
            players = players.Where(p =>
                _db.ShortlistEntries.Any(s => s.RoomId == room.Id && s.PlayerId == p.Id));
        }

        var round = room.AuctionRound;
        var rows = await players
            .Where(p => !_db.Holdings.Any(h => h.RoomId == room.Id && h.PlayerId == p.Id))
            .Where(p => !_db.AuctionPasses.Any(
                a => a.RoomId == room.Id && a.PlayerId == p.Id && a.Round == round))
            .Select(p => new { p.Id, p.NowCost })
            .ToListAsync(ct);

        return rows.Select(r => new NominationCandidate(r.Id, r.NowCost)).ToList();
    }

    /// <summary>
    /// Put a player on the block, or return the one already there.
    /// </summary>
    /// <remarks>
    /// Idempotent on purpose. Every client polls this room every 4s, so a
    /// re-entrant call must not draw a second name; and a host must not be able
    /// to re-roll until a name they like comes up. The only way past an open
    /// nomination is to sell the player or pass on them.
    ///
    /// Returns null when the round is exhausted — the caller decides whether that
    /// means "start the unsold round" or "end the auction".
    /// </remarks>
    public async Task<Player?> NominateAsync(Room room, CancellationToken ct = default)
    {
        if (room.Status != RoomStatus.Auction)
            throw new AuctionValidationException("The auction is not running.");

        if (room.CurrentNominationPlayerId is Guid openId)
        {
            // Defensive: a nomination is cleared on sale and on pass, so a stale
            // one should be impossible. If it happens anyway, re-draw rather than
            // leaving a sold player on the block forever.
            var stillOpen = await _db.Players.FirstOrDefaultAsync(p => p.Id == openId, ct);
            var owned = await _db.Holdings.AnyAsync(
                h => h.RoomId == room.Id && h.PlayerId == openId, ct);
            if (stillOpen is not null && !owned)
                return stillOpen;

            room.CurrentNominationPlayerId = null;
        }

        var candidates = await GetCandidatesAsync(room, ct);
        var pickedId = PickWeighted(candidates, room.Config.NominationPower, _rng);
        if (pickedId is not Guid id)
            return null;

        room.CurrentNominationPlayerId = id;

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = room.Id,
            EventType = "player_nominated",
            Data = JsonSerializer.Serialize(new
            {
                playerId = id,
                round = room.AuctionRound,
                candidates = candidates.Count
            })
        });

        await _db.SaveChangesAsync(ct);
        return await _db.Players.FirstAsync(p => p.Id == id, ct);
    }

    /// <summary>
    /// Nobody bid: record the pass for this round and clear the block. The player
    /// is not gone — they return in the unsold round, and they remain a legal
    /// unsold-pool swap target afterwards regardless.
    /// </summary>
    public async Task<Guid> PassAsync(Room room, CancellationToken ct = default)
    {
        if (room.Status != RoomStatus.Auction)
            throw new AuctionValidationException("The auction is not running.");

        if (room.CurrentNominationPlayerId is not Guid playerId)
            throw new AuctionValidationException("No player is on the block.");

        // The unique index on (RoomId, PlayerId, Round) is the real guarantee;
        // this check just turns a racing double-pass into a no-op instead of a
        // 500 from a constraint violation.
        var already = await _db.AuctionPasses.AnyAsync(
            a => a.RoomId == room.Id && a.PlayerId == playerId && a.Round == room.AuctionRound, ct);
        if (!already)
        {
            _db.AuctionPasses.Add(new AuctionPass
            {
                RoomId = room.Id,
                PlayerId = playerId,
                Round = room.AuctionRound
            });
        }

        room.CurrentNominationPlayerId = null;

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = room.Id,
            EventType = "player_passed",
            Data = JsonSerializer.Serialize(new { playerId, round = room.AuctionRound })
        });

        await _db.SaveChangesAsync(ct);
        return playerId;
    }

    /// <summary>
    /// Everyone passed over in this room, any round. The picker badges these as
    /// unsold so a host searching manually can tell them apart from players who
    /// simply have not come up yet.
    /// </summary>
    public async Task<List<Guid>> GetPassedPlayerIdsAsync(Guid roomId, CancellationToken ct = default) =>
        await _db.AuctionPasses
            .Where(a => a.RoomId == roomId)
            .Select(a => a.PlayerId)
            .Distinct()
            .ToListAsync(ct);
}
