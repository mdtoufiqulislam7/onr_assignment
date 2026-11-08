using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Api.Services;

public class HoldExpirationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HoldExpirationWorker> _logger;

    public HoldExpirationWorker(IServiceProvider serviceProvider, ILogger<HoldExpirationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HoldExpirationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
                var redisService = scope.ServiceProvider.GetRequiredService<IRedisCacheService>();

                var now = DateTime.UtcNow;

                // Find expired holds that are still marked HELD
                var expiredHolds = await dbContext.SeatHolds
                    .Where(h => h.Status == "HELD" && h.ExpiresAt <= now)
                    .ToListAsync(stoppingToken);

                if (expiredHolds.Count > 0)
                {
                    var affectedClassIds = expiredHolds.Select(h => h.ClassId).Distinct().ToList();

                    foreach (var hold in expiredHolds)
                    {
                        hold.Status = "EXPIRED";
                        hold.UpdatedAt = now;
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Auto-expired {Count} seat holds.", expiredHolds.Count);

                    // Invalidate caches for affected classes and listings
                    var prefixes = affectedClassIds.Select(cid => $"class:{cid}").Concat(new[] { "classes:list" }).ToArray();
                    await redisService.RemoveByPrefixAsync(prefixes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing expired holds.");
            }

            // Runs every 1 second for near-real-time seat expiration
            await Task.Delay(1000, stoppingToken);
        }
    }
}

