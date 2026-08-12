using System.Text.Json;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>Thrown for business-rule violations; mapped to HTTP 400 by the controller.</summary>
public class AuctionValidationException : Exception
{
    public AuctionValidationException(string message) : base(message) { }
}

public class AuctionService
{
    private readonly AuctionDbContext _db;
    private readonly ShortlistService _shortlist;

    public AuctionService(AuctionDbContext db, ShortlistService shortlist)
    {
        _db = db;
        _shortlist = shortlist;
    }

    /// <summary>
    /// Record that a player was sold to a participant at a price. Validates
    /// budget, squad size, player availability, and room state. Atomic.
    /// </summary>
    public async Task<AuctionResult> RecordResultAsync(
        Guid roomId,
        Guid playerId,
        Guid participantId,
        int price,
        CancellationToken ct = default)
    {
        if (price < 0)
            throw new AuctionValidationException("Price cannot be negative.");

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new AuctionValidationException("Room not found.");

        if (room.Status != RoomStatus.Auction)
            throw new AuctionValidationException(
                room.Status == RoomStatus.Setup
                    ? "The auction has not started. Start it from the room."
                    : "The auction is over. Squad changes now go through swaps.");

        var participant = await _db.Participants
            .FirstOrDefaultAsync(p => p.Id == participantId && p.RoomId == roomId, ct)
            ?? throw new AuctionValidationException("Participant not in this room.");

        // Player must exist, and belong to the room's pool if one is assigned.
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new AuctionValidationException("Player not found.");
        if (room.PlayerPoolId is Guid poolId && player.PoolId != poolId)
            throw new AuctionValidationException("Player is not in this room's pool.");

        // If the host curated a shortlist, the auction is confined to it. An
        // empty shortlist means no curation happened, so the whole pool stays
        // auctionable — rooms that predate shortlisting are unaffected. Swaps
        // deliberately ignore this; see ShortlistEntry.
        if (!await _shortlist.IsAuctionableAsync(roomId, playerId, ct))
            throw new AuctionValidationException("Player is not on this room's auction shortlist.");

        // Player must not already be sold in this room.
        var alreadySold = await _db.AuctionResults
            .AnyAsync(a => a.RoomId == roomId && a.PlayerId == playerId, ct);
        if (alreadySold)
            throw new AuctionValidationException("Player already sold in this room.");

        // Budget check.
        if (price > participant.BudgetRemaining)
            throw new AuctionValidationException(
                $"Insufficient budget: {participant.BudgetRemaining} remaining, price {price}.");

        // Squad size check.
        var squadCount = await _db.AuctionResults
            .CountAsync(a => a.RoomId == roomId && a.ParticipantId == participantId, ct);
        if (squadCount >= room.Config.SquadSize)
            throw new AuctionValidationException(
                $"Squad full ({room.Config.SquadSize} players).");

        // Next sequence number.
        var maxSeq = await _db.AuctionResults
            .Where(a => a.RoomId == roomId)
            .MaxAsync(a => (int?)a.SequenceNumber, ct) ?? 0;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var result = new AuctionResult
        {
            RoomId = roomId,
            PlayerId = playerId,
            ParticipantId = participantId,
            PurchasePrice = price,
            SequenceNumber = maxSeq + 1
        };
        _db.AuctionResults.Add(result);

        // Open the live squad slot. Auction buys inherit nothing and snapshot the
        // player's current FPL total so future points accrue from here.
        _db.Holdings.Add(new Holding
        {
            RoomId = roomId,
            ParticipantId = participantId,
            PlayerId = playerId,
            AcquiredVia = AcquisitionSource.Auction,
            AcquisitionPoints = player.TotalPoints,
            AcquisitionPrice = price,
            InheritedPoints = 0
        });

        participant.BudgetRemaining -= price;

        // The sale settles whatever was on the block. Clearing it here (rather
        // than making the client ask) is what lets every poller see the block go
        // empty at the same time, and it keeps the manual picker working: a host
        // who sells someone other than the nominated player leaves the
        // nomination standing, which is correct — that player is still unsold.
        if (room.CurrentNominationPlayerId == playerId)
            room.CurrentNominationPlayerId = null;

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = roomId,
            EventType = "result_recorded",
            UserId = null,
            Data = JsonSerializer.Serialize(new { playerId, participantId, price, seq = result.SequenceNumber })
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>Undo the most recent auction result in a room (host action).</summary>
    public async Task<AuctionResult?> UndoLastAsync(Guid roomId, CancellationToken ct = default)
    {
        // Undo used to load no room at all, so it stayed available after the
        // auction ended — where it would refund a buy whose player may since
        // have been swapped away, silently desyncing budgets from Holdings.
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new AuctionValidationException("Room not found.");

        if (room.Status != RoomStatus.Auction)
            throw new AuctionValidationException(
                room.Status == RoomStatus.Setup
                    ? "The auction has not started."
                    : "The auction is over; results can no longer be undone.");

        var last = await _db.AuctionResults
            .Where(a => a.RoomId == roomId)
            .OrderByDescending(a => a.SequenceNumber)
            .FirstOrDefaultAsync(ct);

        if (last is null)
            throw new AuctionValidationException("Nothing to undo.");

        var participant = await _db.Participants
            .FirstAsync(p => p.Id == last.ParticipantId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Refund budget and remove the result.
        participant.BudgetRemaining += last.PurchasePrice;
        _db.AuctionResults.Remove(last);

        // Close the live squad slot opened by this buy.
        var holding = await _db.Holdings.FirstOrDefaultAsync(
            h => h.RoomId == roomId && h.PlayerId == last.PlayerId, ct);
        if (holding is not null)
            _db.Holdings.Remove(holding);

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = roomId,
            EventType = "result_undone",
            Data = JsonSerializer.Serialize(new
            {
                playerId = last.PlayerId,
                participantId = last.ParticipantId,
                price = last.PurchasePrice,
                seq = last.SequenceNumber
            })
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return last;
    }

    /// <summary>All recorded results for a room, in auction order.</summary>
    public async Task<List<AuctionResult>> GetResultsAsync(Guid roomId, CancellationToken ct = default)
    {
        return await _db.AuctionResults
            .Where(a => a.RoomId == roomId)
            .Include(a => a.Player)
            .Include(a => a.Participant)
            .OrderBy(a => a.SequenceNumber)
            .ToListAsync(ct);
    }
}
