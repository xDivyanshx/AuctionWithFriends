namespace AuctionRoom.Domain;

/// <summary>
/// Append-only audit trail for traceability: auction records, undos, swaps,
/// status changes. <see cref="Data"/> holds a JSON snapshot of the event.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RoomId { get; set; }

    /// <summary>e.g. "result_recorded", "result_undone", "swap", "status_changed".</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Actor, if known (typically the host).</summary>
    public Guid? UserId { get; set; }

    /// <summary>JSON payload describing the event.</summary>
    public string Data { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
