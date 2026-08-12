namespace AuctionRoom.Domain;

/// <summary>
/// One player the host passed over during a round of the auction: nominated,
/// drew no bids, host skipped them.
///
/// <para>
/// The auction runs in two rounds. Round 1 is the main pass over the weighted
/// pool; round 2 is the "unsold" round where passed players come back once.
/// Because the host can end round 1 whenever they like, everything still unsold
/// at that moment — passed *or* never nominated — is the round-2 candidate set,
/// so a pass is scoped by <see cref="Round"/> and a player is drawable again in
/// a later round. The unique index on (RoomId, PlayerId, Round) makes a
/// double-pass impossible even if two requests race.
/// </para>
///
/// <para>
/// Passing is not permanent: an unsold player remains a legal target for the
/// unsold-pool swap route later, exactly like a player who was never nominated
/// at all.
/// </para>
/// </summary>
public class AuctionPass
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public int Round { get; set; } = 1;

    public DateTimeOffset PassedAt { get; set; } = DateTimeOffset.UtcNow;
}
