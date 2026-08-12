using AuctionRoom.Api.Dtos;
using AuctionRoom.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>
/// Shared player search used by both the pool browser (shortlist curation) and
/// the room's auction picker. The two differ only in which players they start
/// from, so the filtering, ordering and projection live here rather than being
/// written twice and drifting.
/// </summary>
public static class PlayerQuery
{
    /// <summary>Upper bound on a single page. The full FPL pool is ~573.</summary>
    public const int MaxTake = 600;

    /// <summary>
    /// Narrow a player query. Every filter is optional; an unrecognised position
    /// is ignored rather than rejected, so a stale client cannot 400.
    /// </summary>
    public static IQueryable<Player> Filter(
        IQueryable<Player> query,
        string? search,
        string? position,
        string? club,
        int? minPoints)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%") ||
                (p.Team != null && EF.Functions.ILike(p.Team, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(position) &&
            Enum.TryParse<PlayerPosition>(position, ignoreCase: true, out var pos))
        {
            query = query.Where(p => p.Position == pos);
        }

        // Exact match, not ILike: this comes from a dropdown of the pool's own
        // club names, and a substring match would fold "Manchester City" into a
        // search for "Manchester".
        if (!string.IsNullOrWhiteSpace(club))
        {
            var name = club.Trim();
            query = query.Where(p => p.Team == name);
        }

        if (minPoints is int floor && floor > 0)
        {
            query = query.Where(p => p.TotalPoints >= floor);
        }

        return query;
    }

    /// <summary>
    /// One player, mapped exactly as the picker's list rows are. Delegates to
    /// <see cref="ToResponsesAsync"/> rather than repeating the projection, so the
    /// nomination card and the search results can never disagree about a field.
    /// </summary>
    public static async Task<PlayerResponse?> ToResponseAsync(
        IQueryable<Player> players,
        Guid playerId,
        CancellationToken ct = default)
    {
        var rows = await ToResponsesAsync(players.Where(p => p.Id == playerId), 1, ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>
    /// Materialise a page of results, highest scorers first. Age is computed in
    /// memory because the birthday adjustment does not translate to SQL.
    /// </summary>
    public static async Task<List<PlayerResponse>> ToResponsesAsync(
        IQueryable<Player> query,
        int take,
        CancellationToken ct = default)
    {
        var rows = await query
            .AsNoTracking()
            .OrderByDescending(p => p.TotalPoints)
            .ThenBy(p => p.Name)
            .Take(Math.Clamp(take, 1, MaxTake))
            .Select(p => new
            {
                p.Id,
                p.ExternalId,
                p.Name,
                p.Team,
                p.Position,
                p.PhotoUrl,
                p.TotalPoints,
                p.EventPoints,
                p.BirthDate,
                p.PointsPerGame,
                p.Minutes,
                p.Starts,
                p.GoalsScored,
                p.Assists,
                p.NowCost,
                p.Status,
                p.News
            })
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return rows.Select(p => new PlayerResponse(
            p.Id,
            p.ExternalId,
            p.Name,
            p.Team,
            p.Position.ToString(),
            p.PhotoUrl,
            p.TotalPoints,
            p.EventPoints,
            AgeFrom(p.BirthDate, today),
            p.PointsPerGame,
            p.Minutes,
            p.Starts,
            p.GoalsScored,
            p.Assists,
            p.NowCost,
            p.Status,
            p.News)).ToList();
    }

    /// <summary>
    /// Whole years elapsed, counting the birthday itself. Derived on read rather
    /// than stored so it cannot go stale between syncs. Null when FPL publishes
    /// no birth date for the player (a handful of the pool).
    /// </summary>
    private static int? AgeFrom(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is not { } dob) return null;

        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return age >= 0 ? age : null;
    }
}
