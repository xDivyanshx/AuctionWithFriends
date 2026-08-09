using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>
/// Computes standings using the frozen-at-swap points model. A participant's
/// score is the sum of their best-N squad slots, where each slot's contribution
/// is <c>InheritedPoints + (player.TotalPoints - AcquisitionPoints)</c> — i.e.
/// frozen points carried in by prior swaps, plus points the current player has
/// scored since this participant acquired them.
/// </summary>
public class StandingsService
{
    private readonly AuctionDbContext _db;

    public StandingsService(AuctionDbContext db) => _db = db;

    /// <summary>The contribution a single squad slot makes to standings.</summary>
    public static int SlotContribution(Holding h)
    {
        var current = h.Player?.TotalPoints ?? 0;
        return h.InheritedPoints + (current - h.AcquisitionPoints);
    }

    /// <summary>
    /// Compute the full leaderboard for a room and persist the denormalized
    /// TotalPoints / BestNPoints / Rank onto each Participant for fast reads.
    /// </summary>
    public async Task<List<StandingRow>> ComputeAndPersistAsync(string roomCode, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Participants).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(r => r.Code == roomCode, ct)
            ?? throw new InvalidOperationException("Room not found.");

        var bestN = room.Config.BestN;

        // All live holdings for the room, with current player points.
        var holdings = await _db.Holdings
            .Where(h => h.RoomId == room.Id)
            .Include(h => h.Player)
            .ToListAsync(ct);

        var byParticipant = holdings
            .GroupBy(h => h.ParticipantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<StandingRow>();

        foreach (var p in room.Participants)
        {
            var slots = byParticipant.GetValueOrDefault(p.Id, new List<Holding>());
            var contributions = slots.Select(SlotContribution).OrderByDescending(c => c).ToList();

            var totalPoints = contributions.Sum();
            var bestNPoints = contributions.Take(bestN).Sum();

            // Persist denormalized values (rank set after sorting below).
            p.TotalPoints = totalPoints;
            p.BestNPoints = bestNPoints;

            rows.Add(new StandingRow(
                p.Id,
                p.TeamName,
                p.User?.Name ?? "Unknown",
                Rank: 0, // filled after sort
                bestNPoints,
                totalPoints,
                slots.Count,
                p.BudgetRemaining));
        }

        // Rank by best-N score (desc), tie-break by total points, then team name.
        var ranked = rows
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.TotalPoints)
            .ThenBy(r => r.TeamName)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();

        // Write ranks back to participants.
        var rankByParticipant = ranked.ToDictionary(r => r.ParticipantId, r => r.Rank);
        foreach (var p in room.Participants)
            p.Rank = rankByParticipant.GetValueOrDefault(p.Id);

        await _db.SaveChangesAsync(ct);
        return ranked;
    }

    /// <summary>
    /// A participant's squad with per-player point contributions (drill-down).
    /// Marks which slots count toward the best-N score.
    /// </summary>
    public async Task<ParticipantSquad> GetSquadAsync(string roomCode, Guid participantId, CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct)
            ?? throw new InvalidOperationException("Room not found.");

        var participant = await _db.Participants
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == participantId && p.RoomId == room.Id, ct)
            ?? throw new InvalidOperationException("Participant not in this room.");

        var holdings = await _db.Holdings
            .Where(h => h.ParticipantId == participantId && h.RoomId == room.Id)
            .Include(h => h.Player)
            .ToListAsync(ct);

        // Rank slots by contribution to determine which count toward best-N.
        var ordered = holdings
            .Select(h => new { Holding = h, Contribution = SlotContribution(h) })
            .OrderByDescending(x => x.Contribution)
            .ToList();

        var bestN = room.Config.BestN;
        var slots = ordered.Select((x, i) => new SquadSlot(
            x.Holding.PlayerId,
            x.Holding.Player?.Name ?? "Unknown",
            x.Holding.Player?.Team,
            x.Holding.Player?.Position.ToString() ?? "Unknown",
            x.Holding.Player?.TotalPoints ?? 0,
            x.Holding.AcquisitionPoints,
            x.Holding.InheritedPoints,
            x.Contribution,
            CountsTowardScore: i < bestN,
            x.Holding.AcquiredVia.ToString())).ToList();

        var score = slots.Where(s => s.CountsTowardScore).Sum(s => s.Contribution);
        var total = slots.Sum(s => s.Contribution);

        return new ParticipantSquad(
            participant.Id,
            participant.TeamName,
            participant.User?.Name ?? "Unknown",
            participant.BudgetRemaining,
            participant.Rank,
            score,
            total,
            slots);
    }
}

// --- Standings DTOs ---

public record StandingRow(
    Guid ParticipantId,
    string TeamName,
    string UserName,
    int Rank,
    int Score,        // best-N sum — the standings figure
    int TotalPoints,  // all slots
    int SquadCount,
    int BudgetRemaining);

public record ParticipantSquad(
    Guid ParticipantId,
    string TeamName,
    string UserName,
    int BudgetRemaining,
    int? Rank,
    int Score,
    int TotalPoints,
    List<SquadSlot> Squad);

public record SquadSlot(
    Guid PlayerId,
    string PlayerName,
    string? Team,
    string Position,
    int CurrentPoints,      // player's FPL season total
    int AcquisitionPoints,  // snapshot when acquired
    int InheritedPoints,    // frozen points carried in by prior swaps
    int Contribution,       // what this slot adds to standings
    bool CountsTowardScore, // is it in the best-N?
    string AcquiredVia);
