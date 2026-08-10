using AuctionRoom.Domain;

namespace AuctionRoom.Api.Dtos;

// ---------- Requests ----------

public record CreateRoomRequest(
    string RoomName,
    string HostName,
    string HostTeamName,
    int Budget = 100,
    int SquadSize = 15,
    int BestN = 11,
    string? Season = null);

public record JoinRoomRequest(
    string Name,
    string TeamName);

public record RecordResultRequest(
    Guid PlayerId,
    Guid ParticipantId,
    int Price);

// ---------- Responses ----------

public record RoomResponse(
    Guid Id,
    string Code,
    string Name,
    string Sport,
    string Status,
    Guid HostId,
    RoomConfig Config,
    IReadOnlyList<ParticipantResponse> Participants,
    Guid? PlayerPoolId);

public record ParticipantResponse(
    Guid Id,
    string TeamName,
    string UserName,
    int BudgetRemaining,
    int SquadCount,
    int TotalPoints,
    int BestNPoints,
    int? Rank);

/// <summary>Returned on join — includes the token the client stores to act as this user.</summary>
public record JoinResponse(
    Guid ParticipantId,
    Guid UserId,
    string Token,
    RoomResponse Room);

public record AuctionResultResponse(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    Guid ParticipantId,
    string TeamName,
    int PurchasePrice,
    int SequenceNumber);

/// <summary>
/// A player as the auction picker sees them. <paramref name="Age"/> is derived from
/// the stored birth date on every read, so it cannot go stale; it is null for the
/// players FPL publishes no date for. <paramref name="NowCost"/> is FPL's price in
/// tenths of a million (60 = £6.0m).
/// </summary>
public record PlayerResponse(
    Guid Id,
    string? ExternalId,
    string Name,
    string? Team,
    string Position,
    string? PhotoUrl,
    int TotalPoints,
    int EventPoints,
    int? Age,
    decimal PointsPerGame,
    int Minutes,
    int Starts,
    int GoalsScored,
    int Assists,
    int NowCost,
    string? Status,
    string? News);

// ---------- Shortlist ----------

/// <summary>
/// Host edits the room's auction shortlist. Both lists are optional and both are
/// idempotent, so the client can send whatever the user just toggled without
/// first working out what is already stored.
/// </summary>
public record UpdateShortlistRequest(
    IReadOnlyList<Guid>? Add,
    IReadOnlyList<Guid>? Remove);

/// <summary>
/// The room's shortlist. <paramref name="Curated"/> is false when the list is
/// empty, which means the auction is not restricted at all — the distinction
/// matters to the UI, which otherwise cannot tell "not set up" from "everything
/// removed".
/// </summary>
public record ShortlistResponse(
    IReadOnlyList<Guid> PlayerIds,
    int Count,
    bool Curated);

// ---------- Swap requests ----------

/// <summary>Host records an unsold-pool swap: participant releases one owned player
/// and acquires an unsold one at a host-entered price.</summary>
public record RecordUnsoldSwapRequest(
    Guid ParticipantId,
    Guid PlayerOutId,
    Guid PlayerInId,
    int AcquirePrice);

/// <summary>Host records a P2P swap: two participants trade one player each.
/// Each side's acquire price is the host-entered amount that participant
/// pays for the player they receive.</summary>
public record RecordP2PSwapRequest(
    Guid InitiatorParticipantId,
    Guid CounterpartyParticipantId,
    Guid InitiatorPlayerOutId,   // player leaving the initiator's squad
    Guid CounterpartyPlayerOutId, // player leaving the counterparty's squad
    int InitiatorAcquirePrice,   // what initiator pays for CounterpartyPlayerOut
    int CounterpartyAcquirePrice); // what counterparty pays for InitiatorPlayerOut

// ---------- Swap response ----------

public record SwapResponse(
    Guid Id,
    string Type,
    Guid ParticipantId,
    string ParticipantTeamName,
    Guid? CounterpartyParticipantId,
    string? CounterpartyTeamName,
    Guid PlayerOutId,
    string PlayerOutName,
    Guid PlayerInId,
    string PlayerInName,
    int RefundAmount,
    int AcquirePrice,
    int NetBudgetChange,
    DateTimeOffset SwappedAt,
    DateTimeOffset CooldownUntil);
