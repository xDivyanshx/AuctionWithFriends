using System.Globalization;
using System.Text.Json;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>
/// Client for the official Fantasy Premier League API. Imports player pools
/// and syncs points daily.
/// </summary>
public class FplService
{
    private readonly HttpClient _http;
    private readonly AuctionDbContext _db;
    private const string FplBaseUrl = "https://fantasy.premierleague.com/api/";

    public FplService(HttpClient http, AuctionDbContext db)
    {
        _http = http;
        _db = db;
    }

    /// <summary>
    /// Import or refresh the full FPL player pool for a season. Idempotent:
    /// upserts based on ExternalId, so running twice updates points in place.
    /// Returns the PlayerPool (created or existing).
    /// </summary>
    public async Task<PlayerPool> ImportPoolAsync(string season, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"{FplBaseUrl}bootstrap-static/", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<FplBootstrapResponse>(json)
            ?? throw new InvalidOperationException("Failed to parse FPL response.");

        // Build lookup dictionaries for fast joins.
        var teams = data.Teams.ToDictionary(t => t.Id, t => t.Name);
        var positions = new Dictionary<int, PlayerPosition>
        {
            [1] = PlayerPosition.Goalkeeper,
            [2] = PlayerPosition.Defender,
            [3] = PlayerPosition.Midfielder,
            [4] = PlayerPosition.Forward
        };

        // Find or create the shared public pool for this season.
        var poolName = $"EPL {season}";
        var pool = await _db.PlayerPools.FirstOrDefaultAsync(
            p => p.Name == poolName && p.Sport == Sport.Football, ct);

        if (pool is null)
        {
            pool = new PlayerPool
            {
                Name = poolName,
                Sport = Sport.Football,
                Season = season,
                IsPublic = true
            };
            _db.PlayerPools.Add(pool);
            await _db.SaveChangesAsync(ct); // Ensure pool.Id is assigned before linking players.
        }

        // Upsert each player.
        foreach (var elem in data.Elements)
        {
            var externalId = elem.Id.ToString();
            var player = await _db.Players.FirstOrDefaultAsync(
                p => p.PoolId == pool.Id && p.ExternalId == externalId, ct);

            var fullName = $"{elem.FirstName} {elem.SecondName}".Trim();
            var teamName = teams.GetValueOrDefault(elem.Team, "Unknown");
            var position = positions.GetValueOrDefault(elem.ElementType, PlayerPosition.Unknown);
            // FPL's "photo" field claims a .jpg ("223094.jpg") but the asset is
            // only served as .png — the .jpg URL 403s for every player. Swap the
            // extension rather than trusting the feed. Not every player has an
            // asset at all; those 403 too, and the UI falls back to initials.
            var photoUrl = string.IsNullOrWhiteSpace(elem.Photo)
                ? null
                : $"https://resources.premierleague.com/premierleague/photos/players/110x140/p{Path.GetFileNameWithoutExtension(elem.Photo)}.png";

            if (player is null)
            {
                player = new Player { PoolId = pool.Id, ExternalId = externalId };
                _db.Players.Add(player);
            }

            // Every field is assigned on both the insert and the update path: FPL
            // changes names, clubs, prices and availability mid-season, and one
            // shared block cannot drift the way two branches can.
            player.Name = fullName;
            player.Team = teamName;
            player.Position = position;
            player.PhotoUrl = photoUrl;
            player.TotalPoints = elem.TotalPoints;
            player.EventPoints = elem.EventPoints;
            player.BirthDate = ParseBirthDate(elem.BirthDate);
            player.PointsPerGame = ParsePointsPerGame(elem.PointsPerGame);
            player.Minutes = elem.Minutes;
            player.Starts = elem.Starts ?? 0;
            player.GoalsScored = elem.GoalsScored;
            player.Assists = elem.Assists;
            player.NowCost = elem.NowCost;
            player.Status = elem.Status;
            player.News = elem.News;
        }

        pool.LastSyncedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return pool;
    }

    /// <summary>
    /// FPL sends "1995-09-15", or null for the players it has no date for. An
    /// unparseable value is treated as absent rather than throwing: one odd row
    /// must not fail the whole nightly sync.
    /// </summary>
    private static DateOnly? ParseBirthDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Invariant culture on purpose: a comma-decimal server locale would read
    /// "4.4" as 44.
    /// </summary>
    private static decimal ParsePointsPerGame(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
}
