using System.Text.Json;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class TradingViewWebhookService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AlertDashboardStore _store;
    private readonly DiscordWebhookForwarder _discordWebhookForwarder;
    private readonly ILogger<TradingViewWebhookService> _logger;

    public TradingViewWebhookService(
        AlertDashboardStore store,
        DiscordWebhookForwarder discordWebhookForwarder,
        ILogger<TradingViewWebhookService> logger)
    {
        _store = store;
        _discordWebhookForwarder = discordWebhookForwarder;
        _logger = logger;
    }

    public async Task<WebhookProcessResult> ProcessAsync(string rawPayload, CancellationToken cancellationToken)
    {
        var receivedAtUtc = DateTimeOffset.UtcNow;

        TradingViewWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TradingViewWebhookPayload>(rawPayload, JsonOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "TradingView webhook payload was not valid JSON.");
            return new WebhookProcessResult(false, "Payload is not valid JSON.", null, null, null, false, receivedAtUtc);
        }

        var alert = TryParseAlert(payload, rawPayload, receivedAtUtc);
        if (alert is null)
        {
            return new WebhookProcessResult(false, "Payload does not match the expected Discord embed format.", null, null, null, false, receivedAtUtc);
        }

        await _store.RegisterAlertAsync(alert, cancellationToken);
        var forwarded = await _discordWebhookForwarder.ForwardAsync(rawPayload, alert.Lane, cancellationToken);

        _logger.LogInformation(
            "Processed TradingView alert for {Symbol} {Lane} {Timeframe}. Forwarded={Forwarded}.",
            alert.Symbol,
            alert.Lane,
            alert.Timeframe,
            forwarded);

        return new WebhookProcessResult(true, null, alert.Symbol, alert.Lane, alert.Timeframe, forwarded, receivedAtUtc);
    }

    private static ParsedTradingViewAlert? TryParseAlert(
        TradingViewWebhookPayload? payload,
        string rawPayload,
        DateTimeOffset receivedAtUtc)
    {
        var embed = payload?.Embeds?.FirstOrDefault();
        if (embed is null)
        {
            return null;
        }

        var title = embed.Title?.Trim();
        var description = embed.Description?.Trim();
        var footerText = embed.Footer?.Text?.Trim();
        var symbol = GetFieldValue(embed.Fields, "標的")?.Trim();
        var timeframe = NormalizeTimeframe(GetFieldValue(embed.Fields, "週期"));
        var triggeredAtText = GetFieldValue(embed.Fields, "觸發時間")?.Trim() ?? receivedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var price = GetFieldValue(embed.Fields, "收盤價")?.Trim();

        var lane = DetectLane(title, description, footerText);
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(timeframe) || string.IsNullOrWhiteSpace(lane))
        {
            return null;
        }

        return new ParsedTradingViewAlert(
            symbol,
            timeframe,
            lane,
            lane == "DU" ? "SDU 卡夾" : "SDD 卡夾",
            title ?? $"{lane} webhook",
            description ?? $"{lane} webhook received",
            triggeredAtText,
            receivedAtUtc,
            price,
            footerText,
            rawPayload);
    }

    private static string? GetFieldValue(IEnumerable<DiscordFieldPayload> fields, string name)
    {
        return fields.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string? DetectLane(string? title, string? description, string? footerText)
    {
        var combined = string.Join(" ", new[] { title, description, footerText }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (combined.Contains("DU", StringComparison.OrdinalIgnoreCase))
        {
            return "DU";
        }

        if (combined.Contains("DD", StringComparison.OrdinalIgnoreCase))
        {
            return "DD";
        }

        return null;
    }

    private static string NormalizeTimeframe(string? timeframe)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            return "Unknown";
        }

        var raw = timeframe.Trim().ToUpperInvariant();
        return raw switch
        {
            "60" or "1H" or "1HR" => "1H",
            "90" or "90M" => "90M",
            "120" or "2H" => "2H",
            "180" or "3H" => "3H",
            "240" or "4H" => "4H",
            "360" or "6H" => "6H",
            "480" or "8H" => "8H",
            "720" or "12H" => "12H",
            "1D" or "D" or "1440" => "1D",
            _ => raw.Replace("MIN", "M")
        };
    }
}
