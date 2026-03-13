using System.Net.Http.Headers;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class DiscordWebhookForwarder
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DashboardSettingsStore _settingsStore;
    private readonly ILogger<DiscordWebhookForwarder> _logger;

    public DiscordWebhookForwarder(
        IHttpClientFactory httpClientFactory,
        DashboardSettingsStore settingsStore,
        ILogger<DiscordWebhookForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task<bool> ForwardAsync(string rawPayload, string? lane, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.GetDashboardSettingsAsync(cancellationToken);
        var targetWebhookUrl = ResolveWebhookUrl(settings, lane);
        if (string.IsNullOrWhiteSpace(targetWebhookUrl))
        {
            return false;
        }

        var client = _httpClientFactory.CreateClient(nameof(DiscordWebhookForwarder));
        using var request = new HttpRequestMessage(HttpMethod.Post, targetWebhookUrl);
        request.Content = new StringContent(rawPayload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning("Discord webhook forwarding failed with status code {StatusCode}.", response.StatusCode);
        return false;
    }

    private static string? ResolveWebhookUrl(DashboardRuntimeSettings settings, string? lane)
    {
        return string.Equals(lane, "DD", StringComparison.OrdinalIgnoreCase)
            ? settings.DdWebhookUrl
            : settings.DuWebhookUrl;
    }
}
