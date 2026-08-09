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

    // Navigation
    public ICollection<AuctionResult> AuctionResults { get; set; } = new List<AuctionResult>();
}
