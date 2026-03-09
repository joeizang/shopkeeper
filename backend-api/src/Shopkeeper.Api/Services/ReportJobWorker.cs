using Shopkeeper.Api.Infrastructure;

namespace Shopkeeper.Api.Services;

public sealed class ReportJobWorker(
    IServiceScopeFactory scopeFactory,
    ReportJobChannel jobChannel,
    ILogger<ReportJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reporting = scope.ServiceProvider.GetRequiredService<ReportingService>();

                var pending = await reporting.ClaimNextPendingJob(stoppingToken);
                if (pending is not null)
                {
                    await reporting.ExecuteQueuedReportJob(pending.Id, stoppingToken);
                    // Immediately loop to pick up any further pending jobs.
                    continue;
                }

                // No pending jobs — wait for a channel signal or a 30-second
                // timeout (guards against missed signals after a restart).
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                waitCts.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    await jobChannel.Reader.WaitToReadAsync(waitCts.Token);
                    // Drain any queued signals so the channel stays empty.
                    while (jobChannel.Reader.TryRead(out _)) { }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // 30-second fallback elapsed — loop to re-check the DB.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background report worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
