using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class DailyResetBackgroundService : BackgroundService
{
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxCheckInterval = TimeSpan.FromMinutes(5);

    private readonly DashboardSettingsStore _settingsStore;
    private readonly AlertDashboardStore _dashboardStore;
    private readonly DashboardOptions _options;
    private readonly ILogger<DailyResetBackgroundService> _logger;
    private readonly TimeZoneInfo _timeZone;
    private readonly TimeOnly _resetTime;

    public DailyResetBackgroundService(
        DashboardSettingsStore settingsStore,
        AlertDashboardStore dashboardStore,
        Microsoft.Extensions.Options.IOptions<DashboardOptions> options,
        ILogger<DailyResetBackgroundService> logger)
    {
        _settingsStore = settingsStore;
        _dashboardStore = dashboardStore;
        _options = options.Value;
        _logger = logger;
        _timeZone = ResolveTimeZone(_options.DailyResetTimeZoneId);
        _resetTime = _options.GetDailyResetTime();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Daily reset background service started. Schedule: {Time} ({TimeZoneId}).",
            _resetTime,
            _timeZone.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunScheduledResetIfNeededAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily reset check failed.");
            }

            var delay = GetDelayUntilNextCheck();
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RunScheduledResetIfNeededAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.GetDashboardSettingsAsync(cancellationToken);
        if (!settings.DailyResetEnabled)
        {
            return;
        }

        var utcNow = DateTimeOffset.UtcNow;
        var localNow = TimeZoneInfo.ConvertTime(utcNow, _timeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var resetMoment = localDate.ToDateTime(_resetTime);

        if (localNow.DateTime < resetMoment)
        {
            return;
        }

        if (DateOnly.TryParse(settings.LastAutoResetLocalDate, out var lastResetDate) && lastResetDate >= localDate)
        {
            return;
        }

        _logger.LogInformation(
            "Running scheduled dashboard reset for {LocalDate} at {LocalTime}.",
            localDate,
            localNow);

        await _dashboardStore.ResetDashboardAsync(utcNow, cancellationToken);
        await _settingsStore.MarkDailyResetCompletedAsync(localDate, cancellationToken);
    }

    private TimeSpan GetDelayUntilNextCheck()
    {
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
        var nextCheck = localNow.AddSeconds(30);
        var resetMomentToday = new DateTimeOffset(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            _resetTime.Hour,
            _resetTime.Minute,
            0,
            localNow.Offset);

        if (localNow < resetMomentToday && resetMomentToday - localNow < MaxCheckInterval)
        {
            nextCheck = resetMomentToday;
        }

        var delay = nextCheck - localNow;
        if (delay < MinCheckInterval)
        {
            return MinCheckInterval;
        }

        return delay > MaxCheckInterval ? MaxCheckInterval : delay;
    }

    private static TimeZoneInfo ResolveTimeZone(string? configuredId)
    {
        foreach (var id in new[] { configuredId, "Asia/Taipei", "Taipei Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "UTC+08",
            TimeSpan.FromHours(8),
            "UTC+08",
            "UTC+08");
    }
}
