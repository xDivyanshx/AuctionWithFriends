namespace AuctionRoom.Domain;

/// <summary>Lifecycle of an auction room.</summary>
public enum RoomStatus
{
    Setup,      // Room created, players joining, pool being configured
    Auction,    // One-time auction in progress (host recording results)
    Active,     // Auction done; tournament running, standings updating
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
