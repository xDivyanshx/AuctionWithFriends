using System.Text.Json;
using AuctionRoom.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Infrastructure;

public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<PlayerPool> PlayerPools => Set<PlayerPool>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<AuctionResult> AuctionResults => Set<AuctionResult>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Swap> Swaps => Set<Swap>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Enums are stored as readable strings (configured per-property below).

        b.Entity<User>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Email).HasMaxLength(255);
            e.HasIndex(x => x.Email).IsUnique().HasFilter(null);
        });

        b.Entity<Room>(e =>
        {
            e.Property(x => x.Code).IsRequired().HasMaxLength(10);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Sport).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Season).HasMaxLength(50);

            // RoomConfig persisted as JSON text.
            e.Property(x => x.Config)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<RoomConfig>(v, (JsonSerializerOptions?)null) ?? new RoomConfig());

            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PlayerPool).WithMany().HasForeignKey(x => x.PlayerPoolId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Participant>(e =>
        {
            e.Property(x => x.TeamName).IsRequired().HasMaxLength(255);
            e.Property(x => x.LastSwapMonth).HasMaxLength(7); // "YYYY-MM"
            e.HasIndex(x => new { x.RoomId, x.UserId }).IsUnique();
            e.HasOne(x => x.Room).WithMany(r => r.Participants)
                .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(u => u.Participations)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PlayerPool>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Sport).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Season).HasMaxLength(50);
        });

        b.Entity<Player>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Team).HasMaxLength(255);
            e.Property(x => x.Position).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ExternalId).HasMaxLength(50);
            e.HasIndex(x => new { x.PoolId, x.ExternalId });
            e.HasOne(x => x.Pool).WithMany(p => p.Players)
                .HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuctionResult>(e =>
        {
            // A player can be sold at most once per room.
            e.HasIndex(x => new { x.RoomId, x.PlayerId }).IsUnique();
            e.HasIndex(x => new { x.RoomId, x.SequenceNumber });
            e.HasOne(x => x.Room).WithMany(r => r.AuctionResults)
                .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player).WithMany(p => p.AuctionResults)
                .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Participant).WithMany(p => p.AuctionResults)
                .HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Holding>(e =>
        {
            e.Property(x => x.AcquiredVia).HasConversion<string>().HasMaxLength(20);
            // A player can be held by at most one participant per room at a time.
            e.HasIndex(x => new { x.RoomId, x.PlayerId }).IsUnique();
            e.HasIndex(x => new { x.RoomId, x.ParticipantId });
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Swap>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.RoomId, x.SwappedAt });
            e.HasOne(x => x.Room).WithMany(r => r.Swaps)
                .HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.EventType).IsRequired().HasMaxLength(50);
            e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasIndex(x => x.RoomId);
        });
    }
}
