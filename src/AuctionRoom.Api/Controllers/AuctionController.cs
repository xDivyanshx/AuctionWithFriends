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
    private readonly CallerContext _caller;

    public AuctionController(AuctionDbContext db, AuctionService auctionService, CallerContext caller)
    {
        _db = db;
        _auctionService = auctionService;
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
}
