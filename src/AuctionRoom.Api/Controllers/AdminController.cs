using AuctionRoom.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuctionRoom.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    // One import at a time. The free host runs a single instance, so a static
    // gate is enough to stop a retrying cron — or a manual import fired during a
    // background sync — from starting a second concurrent pass over the same rows.
    private static int _importRunning;

    private readonly FplService _fpl;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        FplService fpl,
        IServiceScopeFactory scopeFactory,
        ILogger<AdminController> logger)
    {
        _fpl = fpl;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Shared secret gate for both admin endpoints. Returns null when the caller
    /// is authorized, otherwise the 401 to return. Both endpoints run the same
    /// full pool import, so they carry the same protection.
    /// </summary>
    private IActionResult? CheckSyncSecret()
    {
        var expectedSecret = Environment.GetEnvironmentVariable("SYNC_SECRET") ?? "change-me-in-production";
        var providedSecret = Request.Headers["X-Sync-Secret"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedSecret) || providedSecret != expectedSecret)
        {
            return Unauthorized(new { error = "Invalid or missing X-Sync-Secret header." });
        }

        return null;
    }

    /// <summary>
    /// Import or refresh the FPL player pool for a season. Idempotent.
    /// Protected by X-Sync-Secret header (configured via SYNC_SECRET env var).
    ///
    /// This one blocks until the import finishes, so it is the endpoint to use
    /// when you want to see the result — run it by hand with a generous client
    /// timeout. The unattended daily job uses POST /api/admin/sync instead.
    /// </summary>
    [HttpPost("import-fpl")]
    public async Task<IActionResult> ImportFpl([FromQuery] string season = "2026-27", CancellationToken ct = default)
    {
        if (CheckSyncSecret() is { } unauthorized) return unauthorized;

        if (Interlocked.CompareExchange(ref _importRunning, 1, 0) == 1)
        {
            return Conflict(new { error = "An import is already in progress." });
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
        finally
        {
            Interlocked.Exchange(ref _importRunning, 0);
        }
    }

    /// <summary>
    /// Daily sync endpoint for updating FPL player points. Called by cron-job.org.
    /// Protected by X-Sync-Secret header (configured via SYNC_SECRET env var).
    ///
    /// Returns 202 immediately and imports in the background. The import takes
    /// minutes against Neon, but cron-job.org's free plan caps a request at 30s,
    /// and a client disconnect cancels the request's token — which would abort the
    /// import at its single closing SaveChanges and write nothing at all. Accepting
    /// the job and letting the response go is what decouples the two clocks.
    ///
    /// Completion is observable via lastSyncedAt on GET /api/pools, and both the
    /// success and failure paths are logged.
    /// </summary>
    [HttpPost("sync")]
    public IActionResult Sync([FromQuery] string season = "2026-27")
    {
        if (CheckSyncSecret() is { } unauthorized) return unauthorized;

        if (Interlocked.CompareExchange(ref _importRunning, 1, 0) == 1)
        {
            return Accepted(new
            {
                status = "already-running",
                message = "An import is already in progress; this request did nothing."
            });
        }

        // Deliberately not awaited, and deliberately given CancellationToken.None:
        // the response returns immediately, so the request's token is cancelled the
        // moment we hand it back. The request scope is disposed with the response
        // too, so the work resolves FplService — and the DbContext behind it — from
        // a scope of its own rather than the one that is about to go away.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var fpl = scope.ServiceProvider.GetRequiredService<FplService>();

                var pool = await fpl.ImportPoolAsync(season, CancellationToken.None);

                _logger.LogInformation(
                    "Background FPL sync finished for {Season}: {PlayerCount} players in pool {PoolId}.",
                    season, pool.Players?.Count ?? 0, pool.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background FPL sync failed for {Season}.", season);
            }
            finally
            {
                Interlocked.Exchange(ref _importRunning, 0);
            }
        });

        return Accepted(new
        {
            status = "started",
            season,
            message = "Sync started in the background. Check lastSyncedAt on GET /api/pools to confirm it landed."
        });
    }
}
