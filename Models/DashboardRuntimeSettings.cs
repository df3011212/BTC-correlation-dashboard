namespace TradingViewWebhookDashboard.Models;

public sealed record DashboardRuntimeSettings
{
    public DashboardRuntimeSettings()
    {
    }

    public DashboardRuntimeSettings(
        string? duWebhookUrl,
        string? ddWebhookUrl,
        bool dailyResetEnabled = true,
        string? lastAutoResetLocalDate = null)
    {
        DuWebhookUrl = duWebhookUrl;
        DdWebhookUrl = ddWebhookUrl;
        DailyResetEnabled = dailyResetEnabled;
        LastAutoResetLocalDate = lastAutoResetLocalDate;
    }

    public string? DuWebhookUrl { get; init; }

    public string? DdWebhookUrl { get; init; }

    public bool DailyResetEnabled { get; init; } = true;

    public string? LastAutoResetLocalDate { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(DuWebhookUrl) ||
        !string.IsNullOrWhiteSpace(DdWebhookUrl);
}

public sealed record SaveDashboardRuntimeSettingsRequest
{
    public string? DuWebhookUrl { get; init; }

    public string? DdWebhookUrl { get; init; }

    public bool DailyResetEnabled { get; init; } = true;
}
