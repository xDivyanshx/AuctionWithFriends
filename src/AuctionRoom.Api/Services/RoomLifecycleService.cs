using System.Text.Json;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>One participant whose squad is short of the room's target.</summary>
public record UnderFilledSquad(Guid ParticipantId, string TeamName, int SquadCount, int Required);

/// <summary>
/// Thrown when ending the auction would leave squads short. Carries the offending
/// squads so the controller can answer 409 with a list the console can render,
/// rather than a bare message the host has to interpret.
/// </summary>
public class UnderFilledSquadException : AuctionValidationException
{
    public IReadOnlyList<UnderFilledSquad> Warnings { get; }

    public UnderFilledSquadException(string message, IReadOnlyList<UnderFilledSquad> warnings)
        : base(message) => Warnings = warnings;
}

/// <summary>
/// The room lifecycle: Setup → Auction → War. Phase 3 replaces the auction's
/// implicit start (the first recorded sale used to flip Setup → Auction
/// silently, so a room was recordable the instant it was created) with explicit
/// host actions.
///
/// <para>
/// The lifecycle is deliberately enforced in one place per operation rather than
/// spread across callers: <see cref="AuctionService"/> records results only
/// during <see cref="RoomStatus.Auction"/>, and <see cref="SwapService"/> only
/// during <see cref="RoomStatus.War"/> — a room in Setup can neither sell nor
/// swap, which was not true before this phase.
/// </para>
/// </summary>
public class RoomLifecycleService
{
    /// <summary>CLAUDE.md §3: a room holds up to 10 participants.</summary>
    public const int MaxParticipants = 10;

    private readonly AuctionDbContext _db;

    public RoomLifecycleService(AuctionDbContext db) => _db = db;

    /// <summary>Lobby → Auction. The host's opening move; closes joining.</summary>
    /// <remarks>
    /// Setup is the last state anyone can join: a participant list is fixed
    /// before money changes hands. The 10-participant cap and the minimum of two
    /// are both enforced here, where they gate the whole auction.
    /// </remarks>
    public async Task<Room> StartAuctionAsync(Room room, CancellationToken ct = default)
    {
        if (room.Status != RoomStatus.Setup)
            throw new AuctionValidationException(
                room.Status == RoomStatus.Auction
                    ? "The auction has already started."
                    : "This room is not in the setup stage.");

        var participantCount = await _db.Participants.CountAsync(p => p.RoomId == room.Id, ct);
        if (participantCount < 2)
            throw new AuctionValidationException("At least 2 participants are needed to start.");
        if (participantCount > MaxParticipants)
            throw new AuctionValidationException(
                $"A room can have at most {MaxParticipants} participants.");

        room.Status = RoomStatus.Auction;

        // The room has been in Setup since creation, so no player was ever drawn
        // for it. Starting at 1 on the first draw is the only consistent option.
        room.AuctionRound = 1;
        room.CurrentNominationPlayerId = null;

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = room.Id,
            EventType = "auction_started",
            Data = JsonSerializer.Serialize(new { participants = participantCount })
        });

        await _db.SaveChangesAsync(ct);
        return room;
    }

    /// <summary>
    /// Auction → War. A room with under-filled squads warns first; the host
    /// passes <c>confirm=true</c> to override. The gap is not an error — the
    /// unsold players remain swap-eligible during War — but the auction ending
    /// mid-round leaves them stranded, so the host should have tried the unsold
    /// round first.
    /// </summary>
    public async Task<Room> EndAuctionAsync(
        Room room,
        bool confirm,
        CancellationToken ct = default)
    {
        if (room.Status != RoomStatus.Auction)
            throw new AuctionValidationException("The auction is not running.");

        var sold = await _db.AuctionResults.CountAsync(a => a.RoomId == room.Id, ct);

        // Squad size is not a column on Participant — live ownership lives in
        // Holdings, which is also what the standings read, so counting there
        // keeps "under-filled" meaning the same thing everywhere.
        var target = room.Config.SquadSize;
        var underFilled = await _db.Participants
            .Where(p => p.RoomId == room.Id)
            .Select(p => new
            {
                p.Id,
                p.TeamName,
                SquadCount = _db.Holdings.Count(h => h.RoomId == room.Id && h.ParticipantId == p.Id)
            })
            .Where(p => p.SquadCount < target)
            .OrderBy(p => p.TeamName)
            .ToListAsync(ct);

        if (underFilled.Count > 0 && !confirm)
            throw new UnderFilledSquadException(
                $"{underFilled.Count} squad{(underFilled.Count == 1 ? " is" : "s are")} " +
                $"under the target of {target}. Confirm to end the auction anyway.",
                underFilled
                    .Select(p => new UnderFilledSquad(p.Id, p.TeamName, p.SquadCount, target))
                    .ToList());

        room.Status = RoomStatus.War;
        room.CurrentNominationPlayerId = null;

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = room.Id,
            EventType = "auction_ended",
            Data = JsonSerializer.Serialize(new
            {
                sold,
                underFilled = underFilled.Count,
                round = room.AuctionRound,
                confirmed = confirm
            })
        });

        await _db.SaveChangesAsync(ct);
        return room;
    }

    /// <summary>
    /// Round 1 → 2, the unsold round. Everything still unsold when the host ends
    /// round 1 — passed over <em>or</em> never nominated — is the round-2
    /// candidate set, so this only has to bump the number: the passes recorded
    /// against round 1 stop matching the current round and every one of those
    /// players becomes drawable again. No rows are deleted, so the history of
    /// what was passed and when survives.
    /// </summary>
    public async Task<Room> NextRoundAsync(Room room, CancellationToken ct = default)
    {
        if (room.Status != RoomStatus.Auction)
            throw new AuctionValidationException("The auction is not running.");
        if (room.AuctionRound >= 2)
            throw new AuctionValidationException(
                "The unsold round is already running. End the auction when you are done.");

        var requeued = await _db.AuctionPasses
            .CountAsync(p => p.RoomId == room.Id && p.Round == room.AuctionRound, ct);

        room.AuctionRound += 1;

        // An open nomination belongs to the round it was drawn in. Carrying it
        // across would leave a player on the block whose pass, if the host now
        // passes them, would be written against a round they were not drawn in.
        room.CurrentNominationPlayerId = null;

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = room.Id,
            EventType = "auction_round_advanced",
            Data = JsonSerializer.Serialize(new { round = room.AuctionRound, requeued })
        });

        await _db.SaveChangesAsync(ct);
        return room;
    }
}
