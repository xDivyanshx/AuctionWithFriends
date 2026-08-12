using System.Text.Json;
using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>
/// Executes mid-tournament 1-for-1 swaps under the frozen-at-swap points model.
///
/// Money (uniform for both swap types, per CLAUDE.md §3):
///   releasing a player refunds what THAT participant paid for it
///   (<see cref="Holding.AcquisitionPrice"/>); acquiring costs the host-entered
///   price; net = refund − cost is applied to the participant's budget, which
///   must stay ≥ 0. For P2P the host enters one acquire price per side and each
///   side's net is computed independently (budgets are per-participant caps, not
///   a shared pool — same as the auction).
///
/// Points (frozen-at-swap): the released slot's accrued value
///   (<see cref="StandingsService.SlotContribution"/>) transfers into the new
///   slot as <see cref="Holding.InheritedPoints"/>; the incoming player's
///   <see cref="Holding.AcquisitionPoints"/> is snapshotted to its current FPL
///   total, so the slot keeps the outgoing player's points-to-date and earns the
///   incoming player's points from here on.
///
/// Gates (all server-side): host-only (enforced in the controller), weekends
/// only (IST), ≤1 swap per participant per calendar month, and a 1-month
/// per-player re-swap cooldown. The clock is read via <see cref="TimeProvider"/>
/// so these gates are testable with a deterministic fake clock.
/// </summary>
public class SwapService
{
    private readonly AuctionDbContext _db;
    private readonly TimeProvider _clock;

    /// <summary>India Standard Time is a fixed +05:30 with no DST — pure offset
    /// arithmetic avoids any dependency on the host's timezone database.</summary>
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public SwapService(AuctionDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Release an owned player and acquire an unsold (unowned) one. The acquiring
    /// participant is refunded the price they paid for the released player and
    /// charged the host-entered <paramref name="acquirePrice"/>.
    /// </summary>
    public async Task<Swap> SwapUnsoldAsync(
        Guid roomId,
        Guid participantId,
        Guid playerOutId,
        Guid playerInId,
        int acquirePrice,
        CancellationToken ct = default)
    {
        if (acquirePrice < 0)
            throw new AuctionValidationException("Acquire price cannot be negative.");
        if (playerOutId == playerInId)
            throw new AuctionValidationException("Cannot swap a player for themselves.");

        var (nowUtc, istNow) = Now();
        RequireWeekend(istNow);
        var currentMonth = istNow.ToString("yyyy-MM");
        var cooldownUntil = nowUtc.AddMonths(1);

        var room = await LoadOpenRoomAsync(roomId, ct);

        var participant = await LoadParticipantAsync(roomId, participantId, ct);
        RequireMonthlyCapAvailable(participant, currentMonth);

        // Ownership: participant must currently hold the outgoing player.
        var holdingOut = await LoadHoldingAsync(roomId, participantId, playerOutId, ct);

        // The incoming player must exist in the room's pool and be unowned.
        var playerIn = await LoadPoolPlayerAsync(room, playerInId, ct);
        await RequireUnownedAsync(roomId, playerInId, ct);

        await RequireNotOnCooldownAsync(roomId, playerOutId, nowUtc, "released", ct);
        await RequireNotOnCooldownAsync(roomId, playerInId, nowUtc, "acquired", ct);

        var refund = holdingOut.AcquisitionPrice;
        var net = refund - acquirePrice;
        if (participant.BudgetRemaining + net < 0)
            throw new AuctionValidationException(
                $"Insufficient budget: {participant.BudgetRemaining} + refund {refund} − price {acquirePrice} would be negative.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        MoveSlot(holdingOut, playerInId, playerIn.TotalPoints, acquirePrice);
        participant.BudgetRemaining += net;
        participant.LastSwapMonth = currentMonth;

        var swap = new Swap
        {
            RoomId = roomId,
            Type = SwapType.UnsoldPool,
            ParticipantId = participantId,
            CounterpartyParticipantId = null,
            PlayerOutId = playerOutId,
            PlayerInId = playerInId,
            RefundAmount = refund,
            AcquirePrice = acquirePrice,
            NetBudgetChange = net,
            SwappedAt = nowUtc,
            CooldownUntil = cooldownUntil
        };
        _db.Swaps.Add(swap);

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = roomId,
            EventType = "swap_unsold",
            Data = JsonSerializer.Serialize(new
            {
                participantId, playerOutId, playerInId,
                refund, acquirePrice, net,
                inheritedPoints = holdingOut.InheritedPoints
            })
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return swap;
    }

    /// <summary>
    /// Two participants trade one player each. The initiator releases
    /// <paramref name="playerOutId"/> (owned by them) and receives
    /// <paramref name="playerInId"/> (owned by the counterparty); the counterparty
    /// does the mirror. Each side is refunded what they paid for the player they
    /// release and charged the host-entered price for the player they receive.
    /// </summary>
    public async Task<Swap> SwapP2PAsync(
        Guid roomId,
        Guid participantId,
        Guid counterpartyId,
        Guid playerOutId,
        Guid playerInId,
        int priceForInitiator,
        int priceForCounterparty,
        CancellationToken ct = default)
    {
        if (priceForInitiator < 0 || priceForCounterparty < 0)
            throw new AuctionValidationException("Acquire prices cannot be negative.");
        if (participantId == counterpartyId)
            throw new AuctionValidationException("A participant cannot swap with themselves.");
        if (playerOutId == playerInId)
            throw new AuctionValidationException("The two players must be different.");

        var (nowUtc, istNow) = Now();
        RequireWeekend(istNow);
        var currentMonth = istNow.ToString("yyyy-MM");
        var cooldownUntil = nowUtc.AddMonths(1);

        await LoadOpenRoomAsync(roomId, ct);

        var initiator = await LoadParticipantAsync(roomId, participantId, ct);
        var counterparty = await LoadParticipantAsync(roomId, counterpartyId, ct);
        RequireMonthlyCapAvailable(initiator, currentMonth);
        RequireMonthlyCapAvailable(counterparty, currentMonth);

        // Ownership: each participant must hold the player they are releasing.
        var holdingInitiatorOut = await LoadHoldingAsync(roomId, participantId, playerOutId, ct);
        var holdingCounterpartyOut = await LoadHoldingAsync(roomId, counterpartyId, playerInId, ct);

        var playerToInitiator = holdingCounterpartyOut.Player!; // player the initiator receives
        var playerToCounterparty = holdingInitiatorOut.Player!; // player the counterparty receives

        await RequireNotOnCooldownAsync(roomId, playerOutId, nowUtc, "released", ct);
        await RequireNotOnCooldownAsync(roomId, playerInId, nowUtc, "released", ct);

        var initiatorRefund = holdingInitiatorOut.AcquisitionPrice;
        var initiatorNet = initiatorRefund - priceForInitiator;
        if (initiator.BudgetRemaining + initiatorNet < 0)
            throw new AuctionValidationException(
                $"Initiator budget would go negative: {initiator.BudgetRemaining} + refund {initiatorRefund} − price {priceForInitiator}.");

        var counterpartyRefund = holdingCounterpartyOut.AcquisitionPrice;
        var counterpartyNet = counterpartyRefund - priceForCounterparty;
        if (counterparty.BudgetRemaining + counterpartyNet < 0)
            throw new AuctionValidationException(
                $"Counterparty budget would go negative: {counterparty.BudgetRemaining} + refund {counterpartyRefund} − price {priceForCounterparty}.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // The two slots trade players, so each one's incoming player is still held
        // by the other slot. The unique index on (RoomId, PlayerId) is checked per
        // row, so neither UPDATE can go first without transiently colliding — EF
        // rejects the pair as a circular dependency. Instead, drop both slots,
        // flush that, then re-insert them with the players exchanged. It all runs
        // inside one transaction, so the momentary "neither owned" state is never
        // visible to another reader.
        var initiatorSlot = NextSlot(holdingInitiatorOut, playerInId, playerToInitiator.TotalPoints, priceForInitiator);
        var counterpartySlot = NextSlot(holdingCounterpartyOut, playerOutId, playerToCounterparty.TotalPoints, priceForCounterparty);

        _db.Holdings.Remove(holdingInitiatorOut);
        _db.Holdings.Remove(holdingCounterpartyOut);
        await _db.SaveChangesAsync(ct);

        _db.Holdings.Add(initiatorSlot);
        _db.Holdings.Add(counterpartySlot);

        initiator.BudgetRemaining += initiatorNet;
        initiator.LastSwapMonth = currentMonth;
        counterparty.BudgetRemaining += counterpartyNet;
        counterparty.LastSwapMonth = currentMonth;

        // One canonical row from the initiator's perspective; it references both
        // players (PlayerOut = initiator's, PlayerIn = counterparty's) so the
        // cooldown query covers both. Full both-sides money is in the audit event.
        var swap = new Swap
        {
            RoomId = roomId,
            Type = SwapType.ParticipantToParticipant,
            ParticipantId = participantId,
            CounterpartyParticipantId = counterpartyId,
            PlayerOutId = playerOutId,
            PlayerInId = playerInId,
            RefundAmount = initiatorRefund,
            AcquirePrice = priceForInitiator,
            NetBudgetChange = initiatorNet,
            SwappedAt = nowUtc,
            CooldownUntil = cooldownUntil
        };
        _db.Swaps.Add(swap);

        _db.AuditEvents.Add(new AuditEvent
        {
            RoomId = roomId,
            EventType = "swap_p2p",
            Data = JsonSerializer.Serialize(new
            {
                initiator = new
                {
                    participantId, playerOutId, receives = playerInId,
                    refund = initiatorRefund, price = priceForInitiator, net = initiatorNet,
                    inheritedPoints = holdingInitiatorOut.InheritedPoints
                },
                counterparty = new
                {
                    participantId = counterpartyId, playerOutId = playerInId, receives = playerOutId,
                    refund = counterpartyRefund, price = priceForCounterparty, net = counterpartyNet,
                    inheritedPoints = holdingCounterpartyOut.InheritedPoints
                }
            })
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return swap;
    }

    // --- Frozen-points slot transfer ---

    /// <summary>
    /// Repoint an existing squad slot to the incoming player, carrying the slot's
    /// current accrued value forward as InheritedPoints and snapshotting the new
    /// player's current total. Mutating the row (rather than delete+insert) keeps
    /// the slot's identity and avoids a transient unique-index conflict.
    /// </summary>
    private static void MoveSlot(Holding slot, Guid incomingPlayerId, int incomingTotalPoints, int acquirePrice)
    {
        slot.InheritedPoints = StandingsService.SlotContribution(slot);
        slot.PlayerId = incomingPlayerId;
        slot.Player = null;
        slot.AcquiredVia = AcquisitionSource.Swap;
        slot.AcquisitionPoints = incomingTotalPoints;
        slot.AcquisitionPrice = acquirePrice;
    }

    /// <summary>
    /// Build a fresh Holding for a player entering a squad, inheriting the accrued
    /// value of the slot being vacated. Used in P2P swaps where both slots must be
    /// deleted and re-inserted to avoid unique-index collision.
    /// </summary>
    private static Holding NextSlot(Holding vacated, Guid incomingPlayerId, int incomingTotalPoints, int acquirePrice)
    {
        return new Holding
        {
            RoomId = vacated.RoomId,
            ParticipantId = vacated.ParticipantId,
            PlayerId = incomingPlayerId,
            AcquiredVia = AcquisitionSource.Swap,
            InheritedPoints = StandingsService.SlotContribution(vacated),
            AcquisitionPoints = incomingTotalPoints,
            AcquisitionPrice = acquirePrice
        };
    }

    // --- Validation helpers ---

    private (DateTimeOffset Utc, DateTimeOffset Ist) Now()
    {
        var utc = _clock.GetUtcNow();
        return (utc, utc.ToOffset(IstOffset));
    }

    private static void RequireWeekend(DateTimeOffset istNow)
    {
        if (istNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            throw new AuctionValidationException(
                $"Swaps are allowed only on weekends (IST). Today is {istNow.DayOfWeek} in IST.");
    }

    private static void RequireMonthlyCapAvailable(Participant p, string currentMonth)
    {
        if (p.LastSwapMonth == currentMonth)
            throw new AuctionValidationException(
                $"{p.TeamName} has already used their swap for {currentMonth}.");
    }

    private async Task<Room> LoadOpenRoomAsync(Guid roomId, CancellationToken ct)
    {
        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new AuctionValidationException("Room not found.");

        // Allowlist, not denylist: swaps exist only after the auction. A room in
        // Setup or Auction must reject them, or the host could "swap" around the
        // auction entirely. Completed is unreachable for now — nothing sets it —
        // but the guard stays for when a season-close is added.
        if (room.Status != RoomStatus.War)
            throw new AuctionValidationException(room.Status switch
            {
                RoomStatus.Setup => "The auction has not started yet.",
                RoomStatus.Auction => "Swaps open only after the auction ends.",
                _ => "This tournament is completed; swaps are closed."
            });

        return room;
    }

    private async Task<Participant> LoadParticipantAsync(Guid roomId, Guid participantId, CancellationToken ct)
    {
        return await _db.Participants
            .FirstOrDefaultAsync(p => p.Id == participantId && p.RoomId == roomId, ct)
            ?? throw new AuctionValidationException("Participant not in this room.");
    }

    private async Task<Holding> LoadHoldingAsync(Guid roomId, Guid participantId, Guid playerId, CancellationToken ct)
    {
        return await _db.Holdings
            .Include(h => h.Player)
            .FirstOrDefaultAsync(h => h.RoomId == roomId && h.ParticipantId == participantId && h.PlayerId == playerId, ct)
            ?? throw new AuctionValidationException("That participant does not own the player being released.");
    }

    private async Task<Player> LoadPoolPlayerAsync(Room room, Guid playerId, CancellationToken ct)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct)
            ?? throw new AuctionValidationException("Incoming player not found.");
        if (room.PlayerPoolId is Guid poolId && player.PoolId != poolId)
            throw new AuctionValidationException("Incoming player is not in this room's pool.");
        return player;
    }

    private async Task RequireUnownedAsync(Guid roomId, Guid playerId, CancellationToken ct)
    {
        var owned = await _db.Holdings.AnyAsync(h => h.RoomId == roomId && h.PlayerId == playerId, ct);
        if (owned)
            throw new AuctionValidationException("Incoming player is already owned in this room.");
    }

    private async Task RequireNotOnCooldownAsync(Guid roomId, Guid playerId, DateTimeOffset now, string verb, CancellationToken ct)
    {
        var blocked = await _db.Swaps.AnyAsync(
            s => s.RoomId == roomId
                 && s.CooldownUntil > now
                 && (s.PlayerOutId == playerId || s.PlayerInId == playerId),
            ct);
        if (blocked)
            throw new AuctionValidationException($"The {verb} player is within its 1-month swap cooldown.");
    }
}
