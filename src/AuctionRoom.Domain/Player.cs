namespace AuctionRoom.Domain;

/// <summary>
/// A real footballer in a pool. Points come from the FPL API and are updated by
/// the daily sync; we ingest FPL totals rather than computing our own scoring.
/// </summary>
public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PoolId { get; set; }
    public PlayerPool? Pool { get; set; }

    /// <summary>FPL element id (as string) — the sync upsert key within a pool.</summary>
    public string? ExternalId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Real club, e.g. "Liverpool".</summary>
    public string? Team { get; set; }

    public PlayerPosition Position { get; set; } = PlayerPosition.Unknown;

    public string? PhotoUrl { get; set; }

    /// <summary>Cumulative FPL points this season.</summary>
    public int TotalPoints { get; set; }

    /// <summary>Points from the latest gameweek.</summary>
    public int EventPoints { get; set; }

    /// <summary>
    /// Date of birth from FPL. Null for 17 of the 573 players — FPL simply does
    /// not publish one for them. Stored rather than an age so it never goes stale;
    /// age is computed on read.
    /// </summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>
    /// FPL's own points-per-game. Arrives as a string ("4.4") and is parsed with
    /// the invariant culture — a comma-decimal locale would otherwise misread it.
    /// </summary>
    public decimal PointsPerGame { get; set; }

    public int Minutes { get; set; }

    /// <summary>Matches started. Read alongside Minutes to tell a regular starter
    /// from a substitute with a good points total.</summary>
    public int Starts { get; set; }

    public int GoalsScored { get; set; }

    public int Assists { get; set; }

    /// <summary>FPL price in tenths of a million: 60 means £6.0m.</summary>
    public int NowCost { get; set; }

    /// <summary>
    /// FPL availability code: a=available, i=injured, d=doubtful, s=suspended,
    /// u=unavailable. Shown in the auction picker so nobody spends half a budget
    /// on an injured player.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>FPL's injury/transfer note. Empty string when there is no news.</summary>
    public string? News { get; set; }

    // Navigation
    public ICollection<AuctionResult> AuctionResults { get; set; } = new List<AuctionResult>();
}
