using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Nightly materialization pass for Driver Scorecards: recomputes today's anchor
/// for every active driver (all windows), which also runs the score-drop alert
/// check. Scores stay "as of last night" between runs — page views reuse the
/// materialized row (compute-if-stale covers first-of-day reads), so a scorecard
/// page load never recomputes months of history live.
/// Per-driver failures are swallowed (logged) so one bad driver never stalls the
/// fleet pass.
/// </summary>
public class DriverScorecardRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DriverScorecardRefreshService> _logger;

    public DriverScorecardRefreshService(IServiceScopeFactory scopeFactory,
        ILogger<DriverScorecardRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        // Run once shortly after startup so a fresh deployment warms the scorecards,
        // then every 6h (daily materialization with drift tolerance).
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Driver scorecard refresh pass failed");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    public async Task RefreshAllAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scorecards = scope.ServiceProvider.GetRequiredService<DriverScorecardService>();

        var driverIds = await db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && d.Status == Domain.Enums.DriverStatus.Active)
            .Select(d => d.Id)
            .ToListAsync(ct);
        if (driverIds.Count == 0) return;

        foreach (var driverId in driverIds)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await scorecards.RecomputeAsync(driverId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scorecard refresh failed for driver {DriverId}", driverId);
            }
        }
    }
}