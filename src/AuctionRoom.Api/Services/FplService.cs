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
            var photoUrl = string.IsNullOrWhiteSpace(elem.Photo)
                ? null
                : $"https://resources.premierleague.com/premierleague/photos/players/110x140/p{elem.Photo}";

            if (player is null)
            {
                player = new Player
                {
                    PoolId = pool.Id,
                    ExternalId = externalId,
                    Name = fullName,
                    Team = teamName,
                    Position = position,
                    PhotoUrl = photoUrl,
                    TotalPoints = elem.TotalPoints,
                    EventPoints = elem.EventPoints
                };
                _db.Players.Add(player);
            }
            else
            {
                // Update in place (name/team can change mid-season; points always update).
                player.Name = fullName;
                player.Team = teamName;
                player.Position = position;
                player.PhotoUrl = photoUrl;
                player.TotalPoints = elem.TotalPoints;
                player.EventPoints = elem.EventPoints;
            }
        }

        pool.LastSyncedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return pool;
    }
}
