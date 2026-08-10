namespace AuctionRoom.Domain;

/// <summary>
/// One player the host has marked as auctionable in a given room.
///
/// <para>
/// The shortlist trims the shared ~573-player FPL pool down to the subset a room
/// actually auctions (typically ~200), so the host is not scrolling the whole
/// league during a live sale.
/// </para>
///
/// <para>
/// Scope is deliberately narrow: this is <b>the auction's</b> universe, not the
/// room's. Swaps may still bring in any player from the pool, shortlisted or not
/// — a participant trading for someone off a smaller club is their own business.
/// So SwapService never consults this table; only recording an auction result does.
/// </para>
///
/// <para>
/// An empty shortlist means "no restriction": every player in the pool is
/// auctionable. That keeps rooms created before shortlisting existed working
/// unchanged, and lets a host skip curation entirely.
/// </para>
/// </summary>
public class ShortlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }
    public Room? Room { get; set; }

    public Guid PlayerId { get; set; }
    public Player? Player { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
