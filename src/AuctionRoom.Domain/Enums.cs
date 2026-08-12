namespace AuctionRoom.Domain;

/// <summary>
/// Lifecycle of an auction room. Every transition is an explicit host action;
/// nothing advances as a side effect of recording a sale.
///
/// <para>
/// Persisted as a string (see AuctionDbContext), so members can be added or
/// renamed in code without a migration — but a rename orphans rows already
/// holding the old literal, so check the table first.
/// </para>
/// </summary>
public enum RoomStatus
{
    Setup,      // Room created, players joining, host curating the shortlist
    Auction,    // Auction running: server nominates, host records sales
    War,        // Auction done; tournament running, standings updating, swaps open
    Completed   // Tournament over
}

/// <summary>Sport type. Football now; Cricket is a future room type.</summary>
public enum Sport
{
    Football,
    Cricket
}

/// <summary>
/// Player position. Values map to FPL element_type:
/// 1=GK, 2=DEF, 3=MID, 4=FWD. Unknown for non-football or unmapped.
/// </summary>
public enum PlayerPosition
{
    Unknown = 0,
    Goalkeeper = 1,
    Defender = 2,
    Midfielder = 3,
    Forward = 4
}
