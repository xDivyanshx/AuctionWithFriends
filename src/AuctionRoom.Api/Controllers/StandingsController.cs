using AuctionRoom.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/rooms/{roomCode}")]
public class StandingsController : ControllerBase
{
    private readonly StandingsService _standings;

    public StandingsController(StandingsService standings)
    {
        _standings = standings;
    }

    /// <summary>
    /// The room leaderboard: each participant's best-N score and rank.
    /// The daily-use page. Computed fresh (≤10 participants) and persisted.
    /// </summary>
    [HttpGet("standings")]
    public async Task<IActionResult> GetStandings(string roomCode, CancellationToken ct)
    {
        try
        {
            var rows = await _standings.ComputeAndPersistAsync(roomCode, ct);
            return Ok(rows);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// A participant's full squad with per-player point contributions and which
    /// slots count toward their best-N score.
    /// </summary>
    [HttpGet("participants/{participantId:guid}")]
    public async Task<IActionResult> GetSquad(string roomCode, Guid participantId, CancellationToken ct)
    {
        try
        {
            var squad = await _standings.GetSquadAsync(roomCode, participantId, ct);
            return Ok(squad);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
