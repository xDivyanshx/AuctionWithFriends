using AuctionRoom.Api.Dtos;
using AuctionRoom.Api.Services;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/rooms/{roomCode}/auction")]
public class AuctionController : ControllerBase
{
    private readonly AuctionDbContext _db;
    private readonly AuctionService _auctionService;
    private readonly NominationService _nomination;
    private readonly RoomLifecycleService _lifecycle;
    private readonly CallerContext _caller;

    public AuctionController(
        AuctionDbContext db,
        AuctionService auctionService,
        NominationService nomination,
        RoomLifecycleService lifecycle,
        CallerContext caller)
    {
        _db = db;
        _auctionService = auctionService;
        _nomination = nomination;
        _lifecycle = lifecycle;
        _caller = caller;
    }

    /// <summary>Record a new auction result (host action).</summary>
    [HttpPost("record")]
    public async Task<ActionResult<AuctionResultResponse>> RecordResult(
        string roomCode,
        [FromBody] RecordResultRequest req,
        CancellationToken ct)
    {
        try
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
            if (room is null)
                return NotFound(new { error = "Room not found." });

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can record results." });

            var result = await _auctionService.RecordResultAsync(
                room.Id, req.PlayerId, req.ParticipantId, req.Price, ct);

            // Reload to get navigation props for response.
            await _db.Entry(result).Reference(r => r.Player).LoadAsync(ct);
            await _db.Entry(result).Reference(r => r.Participant).LoadAsync(ct);

            return Ok(new AuctionResultResponse(
                result.Id,
                result.PlayerId,
                result.Player?.Name ?? "Unknown",
                result.ParticipantId,
                result.Participant?.TeamName ?? "Unknown",
                result.PurchasePrice,
                result.SequenceNumber));
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Undo the last recorded result (host action).</summary>
    [HttpPost("undo")]
    public async Task<ActionResult> UndoLast(string roomCode, CancellationToken ct)
    {
        try
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
            if (room is null)
                return NotFound(new { error = "Room not found." });

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can undo results." });

            var undone = await _auctionService.UndoLastAsync(room.Id, ct);

            // Map to a DTO rather than returning the entity: AuctionResult carries
            // Room/Participant navigations that cycle back on themselves, and
            // System.Text.Json throws on the cycle *after* the undo has committed —
            // the client would see a 500 for an action that actually succeeded.
            AuctionResultResponse? undoneDto = null;
            if (undone is not null)
            {
                var player = await _db.Players
                    .FirstOrDefaultAsync(p => p.Id == undone.PlayerId, ct);
                var participant = await _db.Participants
                    .FirstOrDefaultAsync(p => p.Id == undone.ParticipantId, ct);

                undoneDto = new AuctionResultResponse(
                    undone.Id,
                    undone.PlayerId,
                    player?.Name ?? "Unknown",
                    undone.ParticipantId,
                    participant?.TeamName ?? "Unknown",
                    undone.PurchasePrice,
                    undone.SequenceNumber);
            }

            return Ok(new { message = "Last result undone.", undone = undoneDto });
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Get all recorded results for the room, in auction order.</summary>
    [HttpGet("results")]
    public async Task<ActionResult<IEnumerable<AuctionResultResponse>>> GetResults(
        string roomCode,
        CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        var results = await _auctionService.GetResultsAsync(room.Id, ct);

        return Ok(results.Select(r => new AuctionResultResponse(
            r.Id,
            r.PlayerId,
            r.Player?.Name ?? "Unknown",
            r.ParticipantId,
            r.Participant?.TeamName ?? "Unknown",
            r.PurchasePrice,
            r.SequenceNumber)));
    }

    /// <summary>
    /// Draw the next player onto the block, or return the one already there
    /// (host action). Idempotent: a host cannot re-roll until a name they like
    /// comes up, and a client retry cannot burn a nomination.
    /// </summary>
    /// <remarks>
    /// 204 when the round is exhausted. That is not an error — it is the signal
    /// to advance to the unsold round or end the auction, and the console reads
    /// it to decide which button to offer.
    /// </remarks>
    [HttpPost("nominate")]
    public async Task<ActionResult<PlayerResponse>> Nominate(string roomCode, CancellationToken ct)
    {
        try
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
            if (room is null)
                return NotFound(new { error = "Room not found." });

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can nominate players." });

            var player = await _nomination.NominateAsync(room, ct);
            if (player is null)
                return NoContent();

            var response = await PlayerQuery.ToResponseAsync(_db.Players, player.Id, ct);
            return Ok(response);
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Nobody bid on the player on the block (host action). Clears the block and
    /// records the pass against the current round, so they return in the unsold
    /// round rather than being gone for good.
    /// </summary>
    [HttpPost("pass")]
    public async Task<ActionResult> Pass(string roomCode, CancellationToken ct)
    {
        try
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
            if (room is null)
                return NotFound(new { error = "Room not found." });

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can pass on a player." });

            var passedId = await _nomination.PassAsync(room, ct);
            return Ok(new { passedPlayerId = passedId, round = room.AuctionRound });
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Host closes the main round and opens the unsold round (host action).
    /// Everything still unsold — passed over or never drawn — becomes drawable
    /// again.
    /// </summary>
    [HttpPost("next-round")]
    public async Task<ActionResult> NextRound(string roomCode, CancellationToken ct)
    {
        try
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
            if (room is null)
                return NotFound(new { error = "Room not found." });

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can advance the round." });

            await _lifecycle.NextRoundAsync(room, ct);

            var remaining = await _nomination.GetCandidatesAsync(room, ct);
            return Ok(new { round = room.AuctionRound, candidates = remaining.Count });
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Ids of players passed over in this room, any round — the manual picker
    /// badges them <c>Unsold</c>. Public, and deliberately not part of the room
    /// poll: a late auction can hold a few hundred of these.
    /// </summary>
    [HttpGet("passed")]
    public async Task<ActionResult<PassedPlayersResponse>> GetPassed(
        string roomCode,
        CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        var ids = await _nomination.GetPassedPlayerIdsAsync(room.Id, ct);
        return Ok(new PassedPlayersResponse(room.AuctionRound, ids));
    }
}
