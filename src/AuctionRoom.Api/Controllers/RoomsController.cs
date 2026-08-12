using AuctionRoom.Api.Dtos;
using AuctionRoom.Api.Services;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly AuctionDbContext _db;
    private readonly RoomService _roomService;
    private readonly RoomLifecycleService _lifecycle;
    private readonly CallerContext _caller;

    public RoomsController(
        AuctionDbContext db,
        RoomService roomService,
        RoomLifecycleService lifecycle,
        CallerContext caller)
    {
        _db = db;
        _roomService = roomService;
        _lifecycle = lifecycle;
        _caller = caller;
    }

    /// <summary>Create a new room. Host is created/joined automatically.</summary>
    [HttpPost]
    public async Task<ActionResult<RoomResponse>> CreateRoom(
        [FromBody] CreateRoomRequest req,
        CancellationToken ct)
    {
        var (room, hostParticipant, hostUser) = await _roomService.CreateRoomAsync(
            req.RoomName, req.HostName, req.HostTeamName,
            req.Budget, req.SquadSize, req.BestN, req.Season, ct);

        return Ok(new RoomResponse(
            room.Id,
            room.Code,
            room.Name,
            room.Sport.ToString(),
            room.Status.ToString(),
            room.HostId,
            room.Config,
            new[] {
                new ParticipantResponse(
                    hostParticipant.Id,
                    hostParticipant.TeamName,
                    hostUser.Name,
                    hostParticipant.BudgetRemaining,
                    0,
                    0,
                    0,
                    null)
            },
            room.PlayerPoolId,
            // A brand-new room is in Setup: the auction has not started, so there
            // is no round yet and nothing on the block. Both are written out
            // rather than defaulted so the client's shape never varies.
            room.AuctionRound,
            null));
    }

    /// <summary>Join an existing room.</summary>
    [HttpPost("{roomCode}/join")]
    public async Task<ActionResult<JoinResponse>> JoinRoom(
        string roomCode,
        [FromBody] JoinRoomRequest req,
        CancellationToken ct)
    {
        try
        {
            var (room, participant, user) = await _roomService.JoinRoomAsync(
                roomCode, req.Name, req.TeamName, ct);

            // Simple token: just the user ID for MVP (no signature/expiry).
            var token = user.Id.ToString();

            // Reload room with all participants for the response.
            var fullRoom = await _roomService.GetRoomAsync(roomCode, ct);

            return Ok(new JoinResponse(
                participant.Id,
                user.Id,
                token,
                await MapRoomResponseAsync(fullRoom, ct)));
        }
        catch (AuctionValidationException ex)
        {
            // The room exists but will not take this join — auction already
            // started, or ten participants already in. 400 rather than 404:
            // "not found" would be a lie, and it is the same 400-with-{error}
            // shape every other refused action in the API returns.
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>Get full room state.</summary>
    [HttpGet("{roomCode}")]
    public async Task<ActionResult<RoomResponse>> GetRoom(string roomCode, CancellationToken ct)
    {
        try
        {
            var room = await _roomService.GetRoomAsync(roomCode, ct);
            return Ok(await MapRoomResponseAsync(room, ct));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Host starts the auction: Setup → Auction. This is the point of no return
    /// for joining, so it is a deliberate action rather than a side effect of the
    /// first sale (which is what it used to be).
    /// </summary>
    [HttpPost("{roomCode}/start-auction")]
    public async Task<ActionResult<RoomResponse>> StartAuction(string roomCode, CancellationToken ct)
    {
        try
        {
            var room = await _roomService.GetRoomAsync(roomCode, ct);

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can start the auction." });

            await _lifecycle.StartAuctionAsync(room, ct);
            return Ok(await MapRoomResponseAsync(room, ct));
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Host ends the auction: Auction → War. Answers 409 with the under-filled
    /// squads when any participant is short, unless the request confirms.
    /// </summary>
    /// <remarks>
    /// 409 rather than 400 because it is not a rejection — it is the same request
    /// asked again with the consequences shown. The console needs to tell the two
    /// apart to know whether to render a confirm step or an error.
    /// </remarks>
    [HttpPost("{roomCode}/end-auction")]
    public async Task<ActionResult<RoomResponse>> EndAuction(
        string roomCode,
        [FromBody] EndAuctionRequest? req,
        CancellationToken ct)
    {
        try
        {
            var room = await _roomService.GetRoomAsync(roomCode, ct);

            if (!await _caller.IsHostAsync(HttpContext, room, ct))
                return StatusCode(403, new { error = "Only the host can end the auction." });

            await _lifecycle.EndAuctionAsync(room, req?.Confirm ?? false, ct);
            return Ok(await MapRoomResponseAsync(room, ct));
        }
        catch (UnderFilledSquadException ex)
        {
            return Conflict(new { error = ex.Message, warnings = ex.Warnings });
        }
        catch (AuctionValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Map a loaded room to its wire shape. An instance method rather than a
    /// static one because the nominated player has to be read: it is null for
    /// most of a room's life (all of Setup, all of War, and every gap between
    /// nominations), so fetching it on demand beats Include-ing it into every
    /// room query. <see cref="PlayerQuery"/> does the projection so the card on
    /// the block and the picker's rows cannot disagree about a field.
    /// </summary>
    private async Task<RoomResponse> MapRoomResponseAsync(Domain.Room room, CancellationToken ct)
    {
        PlayerResponse? nomination = null;
        if (room.CurrentNominationPlayerId is Guid nomineeId)
            nomination = await PlayerQuery.ToResponseAsync(_db.Players, nomineeId, ct);

        return new RoomResponse(
            room.Id,
            room.Code,
            room.Name,
            room.Sport.ToString(),
            room.Status.ToString(),
            room.HostId,
            room.Config,
            // Order by join time: the auction console re-renders this list on a
            // poll, and Postgres returns rows in no guaranteed order, so an
            // unsorted list would let teams jump around between refreshes.
            room.Participants.OrderBy(p => p.JoinedAt).Select(p => new ParticipantResponse(
                p.Id,
                p.TeamName,
                p.User?.Name ?? "Unknown",
                p.BudgetRemaining,
                p.AuctionResults?.Count ?? 0, // Squad count from loaded navigation
                p.TotalPoints,
                p.BestNPoints,
                p.Rank)).ToList(),
            room.PlayerPoolId,
            room.AuctionRound,
            nomination);
    }
}
