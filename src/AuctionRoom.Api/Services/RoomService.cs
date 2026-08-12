using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

public class RoomService
{
    private readonly AuctionDbContext _db;

    public RoomService(AuctionDbContext db) => _db = db;

    /// <summary>Create a room. The host is created/retrieved by name and joined automatically.</summary>
    public async Task<(Room room, Participant hostParticipant, User hostUser)> CreateRoomAsync(
        string roomName,
        string hostName,
        string hostTeamName,
        int budget,
        int squadSize,
        int bestN,
        string? season,
        CancellationToken ct = default)
    {
        // Find or create host user (by name — no password for MVP).
        var hostUser = await _db.Users.FirstOrDefaultAsync(u => u.Name == hostName, ct);
        if (hostUser is null)
        {
            hostUser = new User { Name = hostName };
            _db.Users.Add(hostUser);
        }

        // Generate unique room code.
        var code = GenerateRoomCode();
        while (await _db.Rooms.AnyAsync(r => r.Code == code, ct))
            code = GenerateRoomCode();

        // Link to the shared public football pool: prefer a season match, else
        // the most recently synced public pool. Null if none imported yet.
        var pool = await _db.PlayerPools
            .Where(p => p.IsPublic && p.Sport == Sport.Football)
            .Where(p => season == null || p.Season == season)
            .OrderByDescending(p => p.LastSyncedAt)
            .FirstOrDefaultAsync(ct)
            ?? await _db.PlayerPools
                .Where(p => p.IsPublic && p.Sport == Sport.Football)
                .OrderByDescending(p => p.LastSyncedAt)
                .FirstOrDefaultAsync(ct);

        var room = new Room
        {
            Code = code,
            Name = roomName,
            HostId = hostUser.Id,
            Season = season,
            PlayerPoolId = pool?.Id,
            Config = new RoomConfig
            {
                Budget = budget,
                SquadSize = squadSize,
                BestN = bestN
            }
        };
        _db.Rooms.Add(room);

        // Host auto-joins.
        var hostParticipant = new Participant
        {
            RoomId = room.Id,
            UserId = hostUser.Id,
            TeamName = hostTeamName,
            BudgetRemaining = budget
        };
        _db.Participants.Add(hostParticipant);

        await _db.SaveChangesAsync(ct);
        return (room, hostParticipant, hostUser);
    }

    /// <summary>
    /// Join an existing room. Only possible in Setup: once the auction starts the
    /// participant list is fixed, because budgets, squads and the nomination pool
    /// were all sized to the people who were there at the start.
    /// </summary>
    public async Task<(Room room, Participant participant, User user)> JoinRoomAsync(
        string roomCode,
        string userName,
        string teamName,
        CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Participants)
            .FirstOrDefaultAsync(r => r.Code == roomCode, ct)
            ?? throw new InvalidOperationException("Room not found.");

        // AuctionValidationException, not InvalidOperationException: the room was
        // found, so the controller's "not found → 404" catch would be wrong here.
        // A refused join is a rule violation like any other, and answers 400.
        if (room.Status != RoomStatus.Setup)
            throw new AuctionValidationException(
                room.Status == RoomStatus.Auction
                    ? "This room's auction has already started."
                    : "This room's auction is over; no one can join now.");

        // Find or create user.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Name == userName, ct);
        if (user is null)
        {
            user = new User { Name = userName };
            _db.Users.Add(user);
        }

        // Check if already joined. Returning the existing participant keeps a
        // rejoin idempotent, and it must be checked *before* the cap: the tenth
        // participant reopening the page is not an eleventh.
        var existing = await _db.Participants
            .FirstOrDefaultAsync(p => p.RoomId == room.Id && p.UserId == user.Id, ct);
        if (existing != null)
            return (room, existing, user);

        // CLAUDE.md §3 caps a room at 10. Never enforced anywhere until now.
        var count = await _db.Participants.CountAsync(p => p.RoomId == room.Id, ct);
        if (count >= RoomLifecycleService.MaxParticipants)
            throw new AuctionValidationException(
                $"This room is full ({RoomLifecycleService.MaxParticipants} participants).");

        // Create participant.
        var participant = new Participant
        {
            RoomId = room.Id,
            UserId = user.Id,
            TeamName = teamName,
            BudgetRemaining = room.Config.Budget
        };
        _db.Participants.Add(participant);

        await _db.SaveChangesAsync(ct);
        return (room, participant, user);
    }

    /// <summary>Get full room state with participants and their squad counts.</summary>
    public async Task<Room> GetRoomAsync(string roomCode, CancellationToken ct = default)
    {
        return await _db.Rooms
            .Include(r => r.Participants)
                .ThenInclude(p => p.User)
            .Include(r => r.Participants)
                .ThenInclude(p => p.AuctionResults)
            .Include(r => r.Host)
            .FirstOrDefaultAsync(r => r.Code == roomCode, ct)
            ?? throw new InvalidOperationException("Room not found.");
    }

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No O/0/I/1 confusion
        var random = new Random();
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }
}
