using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuctionRoom.Tests;

/// <summary>
/// A clock we control, so the weekend / monthly-cap / cooldown gates can be
/// exercised on any real-world day. <see cref="SwapService"/> takes a
/// <see cref="TimeProvider"/> precisely so these gates are testable.
/// </summary>
public sealed class FakeClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; }

    /// <summary>
    /// Accepts an instant in any offset but stores it normalized to UTC:
    /// <see cref="TimeProvider.GetUtcNow"/> is contractually a UTC-offset value
    /// (as <see cref="TimeProvider.System"/> returns), and Npgsql rejects writing
    /// a non-zero-offset DateTimeOffset to <c>timestamptz</c>.
    /// </summary>
    public FakeClock(DateTimeOffset now) => UtcNow = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <summary>2026-08-08 12:00 IST is a Saturday — the canonical "swaps allowed" instant.</summary>
    public static readonly DateTimeOffset SaturdayIst =
        new(2026, 8, 8, 12, 0, 0, TimeSpan.FromHours(5.5));

    /// <summary>2026-08-07 12:00 IST is a Friday — swaps must be rejected.</summary>
    public static readonly DateTimeOffset FridayIst =
        new(2026, 8, 7, 12, 0, 0, TimeSpan.FromHours(5.5));

    /// <summary>2026-09-05 12:00 IST — a Saturday in the *next* calendar month.</summary>
    public static readonly DateTimeOffset NextMonthSaturdayIst =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.FromHours(5.5));
}

/// <summary>
/// Builds a disposable scenario against the real Neon database: a room, two
/// participants with squads, plus some unowned players in the pool. Everything
/// created is torn down in <see cref="DisposeAsync"/> so repeated runs stay clean
/// and never disturb real tournament data.
/// </summary>
public sealed class SwapScenario : IAsyncDisposable
{
    public required AuctionDbContext Db { get; init; }
    public required Room Room { get; init; }
    public required Participant Alice { get; init; }
    public required Participant Bob { get; init; }
    public required List<Player> Pool { get; init; }
    public required User AliceUser { get; init; }
    public required User BobUser { get; init; }

    public static string ConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("3399ac6b-dac7-4107-8686-1b533d442a75")
            .AddEnvironmentVariables()
            .Build();

        // Use the direct (non-pooled) endpoint: transactions in these tests span
        // multiple statements and the pooled endpoint can hand back a different
        // backend mid-flight.
        return config.GetConnectionString("Direct")
            ?? config.GetConnectionString("Default")
            ?? Environment.GetEnvironmentVariable("AUCTIONROOM_DB")
            ?? throw new InvalidOperationException("No Neon connection string configured.");
    }

    public static AuctionDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql(ConnectionString())
            .Options;
        return new AuctionDbContext(options);
    }

    /// <summary>
    /// Creates a room whose pool is a set of freshly-minted test players, so we
    /// never mutate the shared imported FPL pool. Alice and Bob each hold two
    /// players; two more sit unowned for unsold-pool swaps.
    /// </summary>
    public static async Task<SwapScenario> CreateAsync(
        int aliceBudget = 100,
        int bobBudget = 100)
    {
        var db = NewDb();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var pool = new PlayerPool
        {
            Name = $"test-pool-{suffix}",
            Sport = Sport.Football,
            Season = "test",
            IsPublic = false
        };
        db.PlayerPools.Add(pool);

        // Distinct, known point totals make the frozen-points arithmetic legible.
        var players = new List<Player>();
        var totals = new[] { 100, 80, 60, 40, 30, 20 };
        for (int i = 0; i < totals.Length; i++)
        {
            var p = new Player
            {
                Pool = pool,
                PoolId = pool.Id,
                ExternalId = $"test-{suffix}-{i}",
                Name = $"Test Player {i} ({suffix})",
                Team = "Test FC",
                Position = PlayerPosition.Midfielder,
                TotalPoints = totals[i]
            };
            players.Add(p);
            db.Players.Add(p);
        }

        var aliceUser = new User { Name = $"Alice-{suffix}" };
        var bobUser = new User { Name = $"Bob-{suffix}" };
        db.Users.AddRange(aliceUser, bobUser);

        var room = new Room
        {
            Code = $"T{suffix.ToUpperInvariant()[..5]}",
            Name = $"Swap Test Room {suffix}",
            Host = aliceUser,
            HostId = aliceUser.Id,
            Sport = Sport.Football,
            Season = "test",
            Status = RoomStatus.War,
            PlayerPool = pool,
            PlayerPoolId = pool.Id,
            Config = new RoomConfig { Budget = 100, SquadSize = 15, BestN = 11 }
        };
        db.Rooms.Add(room);

        var alice = new Participant
        {
            Room = room, RoomId = room.Id,
            User = aliceUser, UserId = aliceUser.Id,
            TeamName = $"Alice XI {suffix}",
            BudgetRemaining = aliceBudget
        };
        var bob = new Participant
        {
            Room = room, RoomId = room.Id,
            User = bobUser, UserId = bobUser.Id,
            TeamName = $"Bob XI {suffix}",
            BudgetRemaining = bobBudget
        };
        db.Participants.AddRange(alice, bob);

        // Alice holds players[0] (100 pts, paid 30) and players[1] (80 pts, paid 20).
        // Bob holds players[2] (60 pts, paid 25) and players[3] (40 pts, paid 15).
        // players[4] and players[5] stay unowned for unsold-pool swaps.
        db.Holdings.AddRange(
            Hold(room, alice, players[0], acquisitionPoints: 100, price: 30),
            Hold(room, alice, players[1], acquisitionPoints: 80, price: 20),
            Hold(room, bob, players[2], acquisitionPoints: 60, price: 25),
            Hold(room, bob, players[3], acquisitionPoints: 40, price: 15));

        await db.SaveChangesAsync();

        return new SwapScenario
        {
            Db = db, Room = room, Alice = alice, Bob = bob,
            Pool = players, AliceUser = aliceUser, BobUser = bobUser
        };
    }

    private static Holding Hold(Room room, Participant p, Player player, int acquisitionPoints, int price) =>
        new()
        {
            Room = room, RoomId = room.Id,
            Participant = p, ParticipantId = p.Id,
            Player = player, PlayerId = player.Id,
            AcquiredVia = AcquisitionSource.Auction,
            AcquisitionPoints = acquisitionPoints,
            AcquisitionPrice = price,
            InheritedPoints = 0
        };

    /// <summary>Remove every row this scenario created, children first.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var db = NewDb();
            await db.Holdings.Where(h => h.RoomId == Room.Id).ExecuteDeleteAsync();
            await db.Swaps.Where(s => s.RoomId == Room.Id).ExecuteDeleteAsync();
            await db.AuditEvents.Where(a => a.RoomId == Room.Id).ExecuteDeleteAsync();
            await db.AuctionResults.Where(r => r.RoomId == Room.Id).ExecuteDeleteAsync();
            await db.ShortlistEntries.Where(s => s.RoomId == Room.Id).ExecuteDeleteAsync();
            await db.AuctionPasses.Where(p => p.RoomId == Room.Id).ExecuteDeleteAsync();
            await db.Participants.Where(p => p.RoomId == Room.Id).ExecuteDeleteAsync();
            // Clear the nomination before deleting players: the FK is SetNull, but
            // the room row goes first anyway — this keeps the order honest if that
            // ever changes.
            await db.Rooms.Where(r => r.Id == Room.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CurrentNominationPlayerId, (Guid?)null));
            await db.Rooms.Where(r => r.Id == Room.Id).ExecuteDeleteAsync();

            var poolId = Pool[0].PoolId;
            await db.Players.Where(p => p.PoolId == poolId).ExecuteDeleteAsync();
            await db.PlayerPools.Where(p => p.Id == poolId).ExecuteDeleteAsync();

            var userIds = new[] { AliceUser.Id, BobUser.Id };
            await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        }
        finally
        {
            await Db.DisposeAsync();
        }
    }
}
