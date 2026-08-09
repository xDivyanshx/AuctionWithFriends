namespace AuctionRoom.Domain;

/// <summary>
/// Records that a player was sold to a participant at a price during the
/// one-time auction. This is the source of squad ownership (later edited by
/// <see cref="Swap"/>). A player can appear at most once per room.
/// </summary>
public class AuctionResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public Guid ParticipantId { get; set; }
    public Participant? Participant { get; set; }

    public int PurchasePrice { get; set; }

    /// <summary>Order recorded — used to identify the last result for undo.</summary>
    public int SequenceNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
