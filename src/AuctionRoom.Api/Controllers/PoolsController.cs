using AuctionRoom.Api.Services;
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
    /// List players in a pool, optionally filtered by name/club search, position,
    /// exact club and a minimum points floor. Ordered by total points desc. Used
    /// by the auction picker and by shortlist curation.
    /// </summary>
    [HttpGet("{poolId:guid}/players")]
    public async Task<IActionResult> ListPlayers(
        Guid poolId,
        [FromQuery] string? search,
        [FromQuery] string? position,
        [FromQuery] string? club,
        [FromQuery] int? minPoints,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var query = PlayerQuery.Filter(
            _db.Players.Where(p => p.PoolId == poolId),
            search, position, club, minPoints);

        return Ok(await PlayerQuery.ToResponsesAsync(query, take, ct));
    }

    /// <summary>
    /// Distinct club names in a pool, alphabetical. Feeds the club filter on the
    /// shortlist screen, which must offer exactly the names the data actually
    /// holds — FPL's own spellings, not a hardcoded list that goes stale on
    /// promotion and relegation.
    /// </summary>
    [HttpGet("{poolId:guid}/clubs")]
    public async Task<IActionResult> ListClubs(Guid poolId, CancellationToken ct)
    {
        var clubs = await _db.Players
            .Where(p => p.PoolId == poolId && p.Team != null)
            .Select(p => p.Team!)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);

        return Ok(clubs);
    }
}
