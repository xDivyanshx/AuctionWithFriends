namespace AuctionRoom.Domain;

/// <summary>
/// A user's entry in a specific room: their team, budget, and standings figures.
/// <see cref="TotalPoints"/> / <see cref="BestNPoints"/> / <see cref="Rank"/> are
/// denormalized snapshots recomputed by the daily FPL sync for fast reads on the
/// standings page.
/// </summary>
public class Participant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public int BudgetRemaining { get; set; }

    /// <summary>Sum of ALL owned players' points (informational).</summary>
    public int TotalPoints { get; set; }

    /// <summary>Sum of best-N owned players — the standings score.</summary>
    public int BestNPoints { get; set; }

    /// <summary>1-based rank in the room. Null until first computed.</summary>
    public int? Rank { get; set; }

    /// <summary>YYYY-MM of their last swap; used to enforce the 1/month cap.</summary>
    public string? LastSwapMonth { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<AuctionResult> AuctionResults { get; set; } = new List<AuctionResult>();
}
