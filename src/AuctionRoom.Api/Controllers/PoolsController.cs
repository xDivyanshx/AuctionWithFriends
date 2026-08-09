using AuctionRoom.Api.Dtos;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/pools")]
public class PoolsController : ControllerBase
{
    private readonly AuctionDbContext _db;

    public PoolsController(AuctionDbContext db)
    {
        _db = db;
    }

    /// <summary>List available player pools.</summary>
    [HttpGet]
    public async Task<IActionResult> ListPools(CancellationToken ct)
    {
        var pools = await _db.PlayerPools
            .Select(p => new
            {
                p.Id,
                p.Name,
                Sport = p.Sport.ToString(),
                p.Season,
                p.IsPublic,
                p.LastSyncedAt,
                PlayerCount = p.Players.Count
            })
            .ToListAsync(ct);

        return Ok(pools);
    }

    /// <summary>
    /// List players in a pool, optionally filtered by name/team search and
    /// position. Ordered by total points desc. Used by the auction console.
    /// </summary>
    [HttpGet("{poolId:guid}/players")]
    public async Task<IActionResult> ListPlayers(
        Guid poolId,
        [FromQuery] string? search,
        [FromQuery] string? position,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 600);

        var query = _db.Players.Where(p => p.PoolId == poolId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{term}%") ||
                (p.Team != null && EF.Functions.ILike(p.Team, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(position) &&
            Enum.TryParse<Domain.PlayerPosition>(position, ignoreCase: true, out var pos))
        {
            query = query.Where(p => p.Position == pos);
        }

        var players = await query
            .OrderByDescending(p => p.TotalPoints)
            .Take(take)
            .Select(p => new PlayerResponse(
                p.Id,
                p.ExternalId,
                p.Name,
                p.Team,
                p.Position.ToString(),
                p.PhotoUrl,
                p.TotalPoints,
                p.EventPoints))
            .ToListAsync(ct);

        return Ok(players);
    }
}
