using System.Text.Json;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>
/// Curates which players a room auctions. See <see cref="ShortlistEntry"/> for
/// why this is auction-scoped and not a room-wide universe.
/// </summary>
public class ShortlistService
{
    private readonly AuctionDbContext _db;

    public ShortlistService(AuctionDbContext db) => _db = db;

    /// <summary>
    /// Ids of every shortlisted player, oldest first. An empty list means the
    /// host never curated one, which is treated as "the whole pool".
    /// </summary>
    public async Task<List<Guid>> GetPlayerIdsAsync(Guid roomId, CancellationToken ct = default) =>
        await _db.ShortlistEntries
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.AddedAt)
            .Select(s => s.PlayerId)
            .ToListAsync(ct);

    /// <summary>
    /// Add and remove players in one batch. Both directions are idempotent —
    /// adding a player already on the list, or removing one that is not, is a
    /// no-op rather than an error, so a click that races the poll cannot fail.
    /// An id sent in both lists is contradictory input; removal wins.
    /// </summary>
    /// <remarks>
    /// Removing a player who has already been sold is allowed and harmless: the
    /// sale, the holding and the standings are all recorded independently, and
    /// sold players are hidden from the picker anyway. Note that emptying the
    /// list entirely re-opens the whole pool, per the rule above.
    /// </remarks>
    public async Task<List<Guid>> ApplyAsync(
        Room room,
        IReadOnlyCollection<Guid>? add,
        IReadOnlyCollection<Guid>? remove,
        CancellationToken ct = default)
    {
        var removeIds = (remove ?? []).Distinct().ToList();
        var addIds = (add ?? []).Distinct().Except(removeIds).ToList();

        if (addIds.Count > 0)
        {
            // Only pool members can ever be auctioned, so refuse an off-pool id
            // here rather than accepting it and having RecordResult reject the
            // sale later, when the room is mid-auction and nobody expects it.
            if (room.PlayerPoolId is not Guid poolId)
                throw new AuctionValidationException("This room has no player pool linked.");

            var inPool = await _db.Players
                .Where(p => p.PoolId == poolId && addIds.Contains(p.Id))
                .CountAsync(ct);
            if (inPool != addIds.Count)
                throw new AuctionValidationException("One or more players are not in this room's pool.");

            var alreadyOn = await _db.ShortlistEntries
                .Where(s => s.RoomId == room.Id && addIds.Contains(s.PlayerId))
                .Select(s => s.PlayerId)
                .ToListAsync(ct);

            foreach (var playerId in addIds.Except(alreadyOn))
                _db.ShortlistEntries.Add(new ShortlistEntry { RoomId = room.Id, PlayerId = playerId });
        }

        if (removeIds.Count > 0)
        {
            var doomed = await _db.ShortlistEntries
                .Where(s => s.RoomId == room.Id && removeIds.Contains(s.PlayerId))
                .ToListAsync(ct);
            _db.ShortlistEntries.RemoveRange(doomed);
        }

        if (addIds.Count > 0 || removeIds.Count > 0)
        {
            // Counts only: the ids themselves are already the table's contents,
            // and a 573-id blob per edit would bloat the audit log for nothing.
            _db.AuditEvents.Add(new AuditEvent
            {
                RoomId = room.Id,
                EventType = "shortlist_updated",
                Data = JsonSerializer.Serialize(new { added = addIds.Count, removed = removeIds.Count })
            });
        }

        // SaveChanges is itself transactional, so the add and remove halves land
        // together or not at all.
        await _db.SaveChangesAsync(ct);
        return await GetPlayerIdsAsync(room.Id, ct);
    }

    /// <summary>
    /// Whether this room is allowed to auction this player. Both checks are
    /// index-backed existence probes, so this costs two cheap round-trips on a
    /// path that already makes several.
    /// </summary>
    public async Task<bool> IsAuctionableAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var curated = await _db.ShortlistEntries.AnyAsync(s => s.RoomId == roomId, ct);
        if (!curated) return true;

        return await _db.ShortlistEntries
            .AnyAsync(s => s.RoomId == roomId && s.PlayerId == playerId, ct);
    }
}
