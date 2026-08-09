namespace AuctionRoom.Domain;

/// <summary>
/// A person. For MVP a user is identified by name only (no password).
/// Optional auth fields are reserved for a future username/password layer.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    // Reserved for optional future auth — nullable, unused in MVP.
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<Participant> Participations { get; set; } = new List<Participant>();
}
