namespace AuctionRoom.Domain;

/// <summary>
/// Kind of mid-tournament transaction. Every transaction is 1-for-1, so squad
/// size is preserved. See <see cref="Swap"/>.
/// </summary>
public enum SwapType
{
    /// <summary>Two participants trade one player each (cash may flow either way).</summary>
    ParticipantToParticipant,

    /// <summary>A participant releases a player and acquires an unsold one.</summary>
    UnsoldPool
}

/// <summary>
/// A host-entered, mid-tournament transaction. ALWAYS 1-for-1 (squad size stays
/// constant — nobody can just drop or just add a player).
///
/// Money model:
///   - Releasing a player refunds their original purchase price to the releaser.
///   - Acquiring a player costs a host-entered price (highest external bid).
///   - Net budget change = refund - cost; may be positive or negative but the
///     resulting budget must stay >= 0.
///
/// Business rules (enforced in the service layer, not on the entity):
///   - Only the host may create transactions.
///   - Weekends only (IST / Asia/Kolkata).
///   - Each participant may do at most ONE transaction per calendar month.
///   - Any player involved is locked from re-swap until <see cref="CooldownUntil"/>
///     (swap date + 1 month).
///
/// For <see cref="SwapType.ParticipantToParticipant"/> both sides are recorded:
/// A releases <see cref="PlayerOutId"/> / receives <see cref="PlayerInId"/>;
/// the counterparty is <see cref="CounterpartyParticipantId"/> and does the
/// mirror. <see cref="AcquirePrice"/> / refund handling applies per side.
/// For <see cref="SwapType.UnsoldPool"/> the counterparty is null and
/// <see cref="PlayerInId"/> comes from the unsold pool.
/// </summary>
public class Swap
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public SwapType Type { get; set; }

    /// <summary>The participant initiating (whose squad this row describes).</summary>
    public Guid ParticipantId { get; set; }

    /// <summary>Other participant for P2P swaps; null for unsold-pool swaps.</summary>
    public Guid? CounterpartyParticipantId { get; set; }

    /// <summary>Player leaving <see cref="ParticipantId"/>'s squad.</summary>
    public Guid PlayerOutId { get; set; }

    /// <summary>Player joining <see cref="ParticipantId"/>'s squad.</summary>
    public Guid PlayerInId { get; set; }

    /// <summary>Refund credited for releasing PlayerOut (its purchase price).</summary>
    public int RefundAmount { get; set; }

    /// <summary>Cost paid to acquire PlayerIn (host-entered highest bid).</summary>
    public int AcquirePrice { get; set; }

    /// <summary>Net budget change for the participant = RefundAmount - AcquirePrice.</summary>
    public int NetBudgetChange { get; set; }

    public DateTimeOffset SwappedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>SwappedAt + 1 month. Players locked from re-swap until this.</summary>
    public DateTimeOffset CooldownUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
