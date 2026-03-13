namespace TradingViewWebhookDashboard.Models;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";
    public static readonly IReadOnlyList<string> DefaultTimeframes = ["1H", "90M", "2H", "3H", "4H", "6H", "8H", "12H", "1D"];
    public const string DefaultDailyResetTimeLocal = "07:58";
    public const string DefaultDailyResetTimeZoneId = "Asia/Taipei";

    public List<string> Timeframes { get; set; } = [];

    public int MaxAlertsPerFolder { get; set; } = 12;

    public string StoragePath { get; set; } = "App_Data/dashboard-state.json";

    public string RuntimeSettingsPath { get; set; } = "App_Data/dashboard-runtime-settings.json";

    public string? WebhookSecret { get; set; }

    public string? ForwardDiscordDuWebhookUrl { get; set; }

    public string? ForwardDiscordDdWebhookUrl { get; set; }

    public bool DailyResetEnabledByDefault { get; set; } = true;

    public string DailyResetTimeLocal { get; set; } = DefaultDailyResetTimeLocal;

    public string DailyResetTimeZoneId { get; set; } = DefaultDailyResetTimeZoneId;

    public IReadOnlyList<string> GetConfiguredTimeframes()
    {
        return Timeframes.Count > 0 ? Timeframes : DefaultTimeframes;
    }

    public bool HasDiscordForwardingConfigured()
    {
        return !string.IsNullOrWhiteSpace(ForwardDiscordDuWebhookUrl) || !string.IsNullOrWhiteSpace(ForwardDiscordDdWebhookUrl);
    }

    public TimeOnly GetDailyResetTime()
    {
        return TimeOnly.TryParse(DailyResetTimeLocal, out var parsedTime)
            ? parsedTime
            : TimeOnly.Parse(DefaultDailyResetTimeLocal);
    }
}
