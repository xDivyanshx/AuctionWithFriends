using AuctionRoom.Api.Dtos;
using AuctionRoom.Api.Services;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Controllers;

/// <summary>
/// The room's auction shortlist: which slice of the player pool this room will
/// actually auction. Reading is open to everyone in the room — participants
/// should be able to see what is coming up — but editing is host-only, like
/// every other write in the app.
/// </summary>
[ApiController]
[Route("api/rooms/{roomCode}/shortlist")]
public class ShortlistController : ControllerBase
{
    private readonly AuctionDbContext _db;
    private readonly ShortlistService _shortlist;
    private readonly CallerContext _caller;

    public ShortlistController(
        AuctionDbContext db,
        ShortlistService shortlist,
        CallerContext caller)
    {
        _db = db;
        _shortlist = shortlist;
        _caller = caller;
    }

    /// <summary>Ids of the players this room will auction.</summary>
    [HttpGet]
    public async Task<ActionResult<ShortlistResponse>> Get(string roomCode, CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        var ids = await _shortlist.GetPlayerIdsAsync(room.Id, ct);
        return Ok(new ShortlistResponse(ids, ids.Count, ids.Count > 0));
    }

    /// <summary>
    /// The shortlisted players themselves, with the same filters and shape the
    /// pool endpoint uses. This is what the auction picker searches: it must not
    /// offer a player the room cannot buy. When no shortlist exists the whole
    /// pool is returned, matching the "empty means unrestricted" rule.
    /// </summary>
    [HttpGet("players")]
    public async Task<ActionResult<IEnumerable<PlayerResponse>>> ListPlayers(
        string roomCode,
        [FromQuery] string? search,
        [FromQuery] string? position,
        [FromQuery] string? club,
        [FromQuery] int? minPoints,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        if (room.PlayerPoolId is not Guid poolId)
            return Ok(Array.Empty<PlayerResponse>());

        // A correlated EXISTS against the shortlist, evaluated per room: one
        // query rather than fetching every id and passing them back as a
        // parameter list, which at ~200 entries would be a large IN clause on
        // every keystroke of the picker's search.
        var curated = await _db.ShortlistEntries.AnyAsync(s => s.RoomId == room.Id, ct);

        var players = _db.Players.Where(p => p.PoolId == poolId);
        if (curated)
        {
            players = players.Where(p =>
                _db.ShortlistEntries.Any(s => s.RoomId == room.Id && s.PlayerId == p.Id));
        }

        var query = PlayerQuery.Filter(players, search, position, club, minPoints);
        return Ok(await PlayerQuery.ToResponsesAsync(query, take, ct));
    }

    /// <summary>
    /// Add and/or remove players (host action). Returns the whole list rather
    /// than a delta: the client re-renders from it, so an authoritative snapshot
    /// removes any chance of local state drifting from the server's.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ShortlistResponse>> Update(
        string roomCode,
        [FromBody] UpdateShortlistRequest req,
        CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        if (!await _caller.IsHostAsync(HttpContext, room, ct))
            return StatusCode(403, new { error = "Only the host can edit the shortlist." });

        try
        {
            var ids = await _shortlist.ApplyAsync(room, req.Add, req.Remove, ct);
            return Ok(new ShortlistResponse(ids, ids.Count, ids.Count > 0));
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
