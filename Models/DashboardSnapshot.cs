namespace TradingViewWebhookDashboard.Models;

public sealed record DashboardSnapshot(
    string UpdatedAtUtc,
    string UpdatedAtLocalText,
    int TotalAlerts,
    IReadOnlyList<SymbolDashboardSnapshot> Symbols)
{
    public static DashboardSnapshot Empty(IReadOnlyList<string> _)
    {
        return new DashboardSnapshot(
            DateTimeOffset.UtcNow.ToString("O"),
            "尚未收到",
            0,
            []);
    }
}

public sealed record SymbolDashboardSnapshot(
    string Symbol,
    string? LastAlertAtUtc,
    string LastAlertAtLocalText,
    FolderSnapshot SduFolder,
    FolderSnapshot SddFolder);

public sealed record FolderSnapshot(
    string Lane,
    string DisplayName,
    string Description,
    string TriggeredCountLabel,
    IReadOnlyList<TimeframeSlotSnapshot> Slots,
    IReadOnlyList<AlertCardSnapshot> RecentAlerts);

public sealed record TimeframeSlotSnapshot(
    string Timeframe,
    bool IsTriggered,
    string? TriggeredAtText,
    string? LastTriggeredAtUtc,
    string? LastTriggeredAtLocalText,
    string? Price,
    string? Title);

public sealed record AlertCardSnapshot(
    string Id,
    string Title,
    string Description,
    string Timeframe,
    string? Price,
    string? FooterText,
    string TriggeredAtText,
    string ReceivedAtUtc,
    string ReceivedAtLocalText);

public sealed record WebhookAcceptedResponse(
    string Symbol,
    string Lane,
    string Timeframe,
    bool ForwardedToDiscord,
    DateTimeOffset ReceivedAtUtc);
