namespace WaterBilling.Services.Recovery;

// Runs in the background while the app is open: once at start-up and then every 6 hours it removes
// for good whatever has been in Recovery for more than 30 days (RecoveryService.RemoveExpiredAsync).
public class RecoveryCleanupJob(IServiceScopeFactory scopes, ILogger<RecoveryCleanupJob> log) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var removed = await scope.ServiceProvider.GetRequiredService<RecoveryService>().RemoveExpiredAsync();
                if (removed > 0) log.LogInformation("Recovery clean-up removed {Count} record(s) older than 30 days.", removed);
            }
            catch (Exception ex)
            {
                // Never stop the app because of the clean-up; it tries again next time.
                log.LogError(ex, "Recovery clean-up failed.");
            }
            try { await Task.Delay(Every, stoppingToken); } catch (TaskCanceledException) { }
        }
    }
}
