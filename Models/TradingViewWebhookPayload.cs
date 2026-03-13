using System.Text.Json.Serialization;

namespace TradingViewWebhookDashboard.Models;

public sealed class TradingViewWebhookPayload
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbedPayload> Embeds { get; init; } = [];
}

public sealed class DiscordEmbedPayload
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("color")]
    public int? Color { get; init; }

    [JsonPropertyName("fields")]
    public List<DiscordFieldPayload> Fields { get; init; } = [];

    [JsonPropertyName("footer")]
    public DiscordFooterPayload? Footer { get; init; }
}

public sealed class DiscordFieldPayload
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("inline")]
    public bool Inline { get; init; }
}

public sealed class DiscordFooterPayload
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

public sealed record ParsedTradingViewAlert(
    string Symbol,
    string Timeframe,
    string Lane,
    string FolderDisplayName,
    string Title,
    string Description,
    string TriggeredAtText,
    DateTimeOffset ReceivedAtUtc,
    string? Price,
    string? FooterText,
    string RawPayload);

public sealed record WebhookProcessResult(
    bool Success,
    string? ErrorMessage,
    string? Symbol,
    string? Lane,
    string? Timeframe,
    bool ForwardedToDiscord,
    DateTimeOffset ReceivedAtUtc);
