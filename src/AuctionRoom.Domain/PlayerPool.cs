namespace AuctionRoom.Domain;

/// <summary>
/// A named set of players imported from a source (e.g. "EPL 2026-27" from the
/// FPL API). Rooms auction from one pool.
/// </summary>
public class PlayerPool
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public Sport Sport { get; set; } = Sport.Football;

    public string? Season { get; set; }

    public bool IsPublic { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last time points were synced from the source API.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    // Navigation
    public ICollection<Player> Players { get; set; } = new List<Player>();
}
