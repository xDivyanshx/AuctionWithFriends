using AuctionRoom.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly FplService _fpl;

    public AdminController(FplService fpl)
    {
        _fpl = fpl;
    }

    /// <summary>
    /// Import or refresh the FPL player pool for a season. Idempotent.
    /// TODO: Protect with a shared secret header for production.
    /// </summary>
    [HttpPost("import-fpl")]
    public async Task<IActionResult> ImportFpl([FromQuery] string season = "2026-27", CancellationToken ct = default)
    {
        try
        {
            var pool = await _fpl.ImportPoolAsync(season, ct);
            var playerCount = pool.Players?.Count ?? 0;

            return Ok(new
            {
                poolId = pool.Id,
                name = pool.Name,
                season = pool.Season,
                playerCount,
                lastSyncedAt = pool.LastSyncedAt
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = "Failed to fetch from FPL API", details = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Import failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Daily sync endpoint for updating FPL player points. Called by cron-job.org.
    /// Protected by X-Sync-Secret header (configured via SYNC_SECRET env var).
    /// </summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] string season = "2026-27", CancellationToken ct = default)
    {
        // Check shared secret.
        var expectedSecret = Environment.GetEnvironmentVariable("SYNC_SECRET") ?? "change-me-in-production";
        var providedSecret = Request.Headers["X-Sync-Secret"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedSecret) || providedSecret != expectedSecret)
        {
            return Unauthorized(new { error = "Invalid or missing X-Sync-Secret header." });
        }

        try
        {
            var pool = await _fpl.ImportPoolAsync(season, ct);
            var playerCount = pool.Players?.Count ?? 0;

            return Ok(new
            {
                poolId = pool.Id,
                name = pool.Name,
                season = pool.Season,
                playerCount,
                lastSyncedAt = pool.LastSyncedAt,
                message = "Sync complete"
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = "Failed to fetch from FPL API", details = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Sync failed", details = ex.Message });
        }
    }
}
