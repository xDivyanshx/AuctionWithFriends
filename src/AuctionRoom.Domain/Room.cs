namespace AuctionRoom.Domain;

/// <summary>
/// An auction room / tournament. Single room for MVP, up to 10 participants.
/// Config values (budget, squad size, best-N) live in <see cref="RoomConfig"/>,
/// stored as JSON so rules can evolve without schema changes.
/// </summary>
public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short shareable join code, e.g. "ABC123". Unique.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public Guid HostId { get; set; }
    public User? Host { get; set; }

    public Sport Sport { get; set; } = Sport.Football;

    /// <summary>e.g. "2026-27".</summary>
    public string? Season { get; set; }

    public RoomStatus Status { get; set; } = RoomStatus.Setup;

    /// <summary>
    /// Which pass of the auction is running: 1 = main, 2 = the unsold round.
    /// Scopes <see cref="AuctionPass"/> rows, so a player passed over in round 1
    /// becomes drawable again in round 2 without deleting any history.
    /// Meaningless outside <see cref="RoomStatus.Auction"/>.
    /// </summary>
    public int AuctionRound { get; set; } = 1;

    /// <summary>
    /// The player currently on the block, or null when nobody is. Server-held on
    /// purpose: every client polls the room, so this is what makes them all show
    /// the same name at the same time.
    /// </summary>
    public Guid? CurrentNominationPlayerId { get; set; }
    public Player? CurrentNominationPlayer { get; set; }

    /// <summary>Tournament rules. Persisted as a JSON column.</summary>
    public RoomConfig Config { get; set; } = new();

    /// <summary>Which player pool this room auctions from.</summary>
    public Guid? PlayerPoolId { get; set; }
    public PlayerPool? PlayerPool { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<Participant> Participants { get; set; } = new List<Participant>();
    public ICollection<AuctionResult> AuctionResults { get; set; } = new List<AuctionResult>();
    public ICollection<Swap> Swaps { get; set; } = new List<Swap>();
}

/// <summary>
/// Tournament configuration, stored as JSON on <see cref="Room"/>.
/// Constraints are deferred for MVP but modeled here so we can enforce later
/// without a migration.
/// </summary>
public class RoomConfig
{
    /// <summary>Starting budget per participant.</summary>
    public int Budget { get; set; } = 100;

    /// <summary>Total players each participant buys, e.g. 15.</summary>
    public int SquadSize { get; set; } = 15;

    /// <summary>How many top scorers count toward standings, e.g. 11.</summary>
    public int BestN { get; set; } = 11;

    /// <summary>Optional constraints (not enforced in MVP).</summary>
    public SquadConstraints? Constraints { get; set; }

    /// <summary>
    /// Sharpness of the weighted nomination draw: weight = NowCost^k. The decline
    /// is emergent — expensive players leave the pool early, so the remaining
    /// pool's average price falls on its own. k=1 is proportional to price and
    /// quite flat; k=6 puts most of the mass on the top slice. Tunable without a
    /// migration because this whole object is a JSON column.
    ///
    /// <para>
    /// 6 was measured, not guessed: against the live 577-player pool it draws a
    /// top-20-by-price player first 64% of the time, has half the top 20 on the
    /// block within 40 nominations and 95% within 150, and still leaves 11% of
    /// first draws to the sub-£5.5m tail. k=7+ starts starving that tail.
    /// </para>
    /// </summary>
    public double NominationPower { get; set; } = 6;
}

/// <summary>Optional squad constraints — reserved, not enforced in MVP.</summary>
public class SquadConstraints
{
    public int? MaxFromSameTeam { get; set; }
    public Dictionary<string, PositionLimit>? PositionLimits { get; set; }
}

public class PositionLimit
{
    public int Min { get; set; }
    public int Max { get; set; }
}
