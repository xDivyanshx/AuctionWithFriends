using AuctionRoom.Api.Dtos;
using AuctionRoom.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/rooms")]
public class RoomsController : ControllerBase
{
    private readonly RoomService _roomService;

    public RoomsController(RoomService roomService) => _roomService = roomService;

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
            room.PlayerPoolId));
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
                MapRoomResponse(fullRoom)));
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
            return Ok(MapRoomResponse(room));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    private static RoomResponse MapRoomResponse(Domain.Room room)
    {
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
            room.PlayerPoolId);
    }
}
