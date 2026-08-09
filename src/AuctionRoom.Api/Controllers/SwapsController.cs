using AuctionRoom.Api.Dtos;
using AuctionRoom.Api.Services;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Controllers;

/// <summary>
/// Host-only mid-tournament swaps. All business gates (weekend, monthly cap,
/// cooldown, ownership, budget) live in <see cref="SwapService"/>; this
/// controller only enforces host permission and shapes the response.
/// </summary>
[ApiController]
[Route("api/rooms/{roomCode}/swaps")]
public class SwapsController : ControllerBase
{
    private readonly AuctionDbContext _db;
    private readonly SwapService _swaps;
    private readonly CallerContext _caller;

    public SwapsController(AuctionDbContext db, SwapService swaps, CallerContext caller)
    {
        _db = db;
        _swaps = swaps;
        _caller = caller;
    }

    /// <summary>Record an unsold-pool swap (host action).</summary>
    [HttpPost("unsold")]
    public async Task<ActionResult<SwapResponse>> RecordUnsold(
        string roomCode,
        [FromBody] RecordUnsoldSwapRequest req,
        CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        if (!await _caller.IsHostAsync(HttpContext, room, ct))
            return StatusCode(403, new { error = "Only the host can record swaps." });

        try
        {
            var swap = await _swaps.SwapUnsoldAsync(
                room.Id, req.ParticipantId, req.PlayerOutId, req.PlayerInId, req.AcquirePrice, ct);
            return Ok(await ToResponseAsync(swap, ct));
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Record a participant-to-participant swap (host action).</summary>
    [HttpPost("p2p")]
    public async Task<ActionResult<SwapResponse>> RecordP2P(
        string roomCode,
        [FromBody] RecordP2PSwapRequest req,
        CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        if (!await _caller.IsHostAsync(HttpContext, room, ct))
            return StatusCode(403, new { error = "Only the host can record swaps." });

        try
        {
            var swap = await _swaps.SwapP2PAsync(
                room.Id,
                req.InitiatorParticipantId,
                req.CounterpartyParticipantId,
                req.InitiatorPlayerOutId,
                req.CounterpartyPlayerOutId,
                req.InitiatorAcquirePrice,
                req.CounterpartyAcquirePrice,
                ct);
            return Ok(await ToResponseAsync(swap, ct));
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>All swaps recorded in a room, most recent first.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SwapResponse>>> ListSwaps(string roomCode, CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Code == roomCode, ct);
        if (room is null)
            return NotFound(new { error = "Room not found." });

        var swaps = await _db.Swaps
            .Where(s => s.RoomId == room.Id)
            .OrderByDescending(s => s.SwappedAt)
            .ToListAsync(ct);

        var responses = new List<SwapResponse>(swaps.Count);
        foreach (var s in swaps)
            responses.Add(await ToResponseAsync(s, ct));

        return Ok(responses);
    }

    /// <summary>Resolve player/participant names for a swap row.</summary>
    private async Task<SwapResponse> ToResponseAsync(Swap s, CancellationToken ct)
    {
        var names = await _db.Participants
            .Where(p => p.Id == s.ParticipantId || (s.CounterpartyParticipantId != null && p.Id == s.CounterpartyParticipantId))
            .Select(p => new { p.Id, p.TeamName })
            .ToListAsync(ct);
        var players = await _db.Players
            .Where(p => p.Id == s.PlayerOutId || p.Id == s.PlayerInId)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(ct);

        string participantName = names.FirstOrDefault(n => n.Id == s.ParticipantId)?.TeamName ?? "Unknown";
        string? counterpartyName = s.CounterpartyParticipantId is Guid cp
            ? names.FirstOrDefault(n => n.Id == cp)?.TeamName
            : null;
        string outName = players.FirstOrDefault(p => p.Id == s.PlayerOutId)?.Name ?? "Unknown";
        string inName = players.FirstOrDefault(p => p.Id == s.PlayerInId)?.Name ?? "Unknown";

        return new SwapResponse(
            s.Id,
            s.Type.ToString(),
            s.ParticipantId,
            participantName,
            s.CounterpartyParticipantId,
            counterpartyName,
            s.PlayerOutId,
            outName,
            s.PlayerInId,
            inName,
            s.RefundAmount,
            s.AcquirePrice,
            s.NetBudgetChange,
            s.SwappedAt,
            s.CooldownUntil);
    }
}
