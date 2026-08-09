using AuctionRoom.Domain;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuctionRoom.Api.Services;

/// <summary>
/// Minimal MVP auth: the client sends the user id (returned on join) in the
/// "X-User-Id" header. This resolves the caller and checks host permission.
/// NOT secure (no signature/expiry) — to be replaced by signed tokens later.
/// See CLAUDE.md §10.
/// </summary>
public class CallerContext
{
    private readonly AuctionDbContext _db;

    public CallerContext(AuctionDbContext db) => _db = db;

    /// <summary>Reads the caller's user id from the X-User-Id header, or null.</summary>
    public Guid? GetUserId(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue("X-User-Id", out var raw)
            && Guid.TryParse(raw, out var id))
        {
            return id;
        }
        return null;
    }

    /// <summary>True if the caller is the host of the given room.</summary>
    public async Task<bool> IsHostAsync(HttpContext http, Room room, CancellationToken ct = default)
    {
        var userId = GetUserId(http);
        return userId.HasValue && room.HostId == userId.Value;
    }
}
