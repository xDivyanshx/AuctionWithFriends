namespace AuctionRoom.Domain;

/// <summary>
/// How a participant came to own a player in their current squad.
/// </summary>
public enum AcquisitionSource
{
    Auction = 0,
    Swap = 1
}

/// <summary>
/// A single live squad slot: one player a participant <b>currently owns</b>.
/// This is the source of truth for present-day ownership and for standings.
///
/// Points attribution follows the "frozen-at-swap" rule. A slot's earned points =
/// <see cref="InheritedPoints"/> + (player's current FPL total − <see cref="AcquisitionPoints"/>).
/// That is: whatever frozen value was carried into this slot by prior swaps, plus
/// only the points the current player has scored <b>since this participant acquired them</b>.
///
/// <para>
/// On an auction buy: a Holding is opened with InheritedPoints = 0 and
/// AcquisitionPoints = the player's FPL total at that moment (≈ 0 pre-season).
/// </para>
/// <para>
/// On a swap (give A → get B): A's Holding is closed and its slot value at that
/// instant becomes B's InheritedPoints; B's Holding opens with AcquisitionPoints
/// set to B's current FPL total. The seller's frozen contribution thus stays in
/// their own new slot, and the buyer starts B from zero-delta.
/// </para>
///
/// <see cref="AuctionResult"/> and <see cref="Swap"/> remain immutable historical
/// ledgers of <i>what happened</i>; Holding is the mutable <i>current truth</i>.
/// </summary>
public class Holding
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid ParticipantId { get; set; }
    public Participant? Participant { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public AcquisitionSource AcquiredVia { get; set; } = AcquisitionSource.Auction;

    /// <summary>Player's FPL total points at the moment this slot was acquired.</summary>
    public int AcquisitionPoints { get; set; }

    /// <summary>
    /// Budget (cap) actually spent to acquire the current player of this slot.
    /// For an auction buy this is the purchase price; for a swap-in it is the
    /// host-entered acquire price. Refunded to the participant if this slot is
    /// later released in a swap, so refunds stay correct across swap chains.
    /// </summary>
    public int AcquisitionPrice { get; set; }

    /// <summary>
    /// Frozen points carried into this slot by prior players swapped out of it.
    /// Zero for auction buys.
    /// </summary>
    public int InheritedPoints { get; set; }

    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;
}
