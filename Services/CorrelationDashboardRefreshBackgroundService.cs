using Microsoft.Extensions.Options;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class CorrelationDashboardRefreshBackgroundService : BackgroundService
{
    private readonly BitgetCorrelationService _bitgetCorrelationService;
    private readonly CorrelationDashboardStore _store;
    private readonly CorrelationDashboardOptions _options;
    private readonly ILogger<CorrelationDashboardRefreshBackgroundService> _logger;

    public CorrelationDashboardRefreshBackgroundService(
        BitgetCorrelationService bitgetCorrelationService,
        CorrelationDashboardStore store,
        IOptions<CorrelationDashboardOptions> options,
        ILogger<CorrelationDashboardRefreshBackgroundService> logger)
    {
        _bitgetCorrelationService = bitgetCorrelationService;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Correlation dashboard refresh service started. Interval: {IntervalMinutes} minutes.",
            _options.GetRefreshInterval().TotalMinutes);

        await RefreshSnapshotAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.GetRefreshInterval());
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshSnapshotAsync(stoppingToken);
        }
    }

    private async Task RefreshSnapshotAsync(CancellationToken cancellationToken)
    {
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            var snapshot = await _bitgetCorrelationService.BuildSnapshotAsync(cancellationToken);
            await _store.SaveSnapshotAsync(snapshot, cancellationToken);

            _logger.LogInformation(
                "Correlation dashboard refreshed successfully. {Count} results matched.",
                snapshot.MatchedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Correlation dashboard refresh failed.");
            await _store.RecordRefreshFailureAsync(ex.Message, attemptedAtUtc, cancellationToken);
        }
    }
}
