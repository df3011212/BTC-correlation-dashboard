using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using TradingViewWebhookDashboard.Models;
using TradingViewWebhookDashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(BitgetCorrelationService.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.bitget.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddProblemDetails();
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));
builder.Services.Configure<CorrelationDashboardOptions>(builder.Configuration.GetSection(CorrelationDashboardOptions.SectionName));
builder.Services.AddSingleton<DashboardStreamBroker>();
builder.Services.AddSingleton<DashboardSettingsStore>();
builder.Services.AddSingleton<AlertDashboardStore>();
builder.Services.AddSingleton<CorrelationDashboardStore>();
builder.Services.AddSingleton<DiscordWebhookForwarder>();
builder.Services.AddSingleton<BitgetCorrelationService>();
builder.Services.AddSingleton<TradingViewWebhookService>();
builder.Services.AddHostedService<DailyResetBackgroundService>();
builder.Services.AddHostedService<CorrelationDashboardRefreshBackgroundService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/api/dashboard", async Task<Ok<DashboardSnapshot>> (AlertDashboardStore store, CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetSnapshotAsync(cancellationToken);
    return TypedResults.Ok(snapshot);
});

app.MapGet("/api/correlation-dashboard", async Task<Ok<CorrelationDashboardSnapshot>> (
    CorrelationDashboardStore store,
    CancellationToken cancellationToken) =>
{
    var snapshot = await store.GetSnapshotAsync(cancellationToken);
    return TypedResults.Ok(snapshot);
});

app.MapDelete("/api/alerts/{alertId}", async Task<Results<NoContent, NotFound>> (
    string alertId,
    AlertDashboardStore store,
    CancellationToken cancellationToken) =>
{
    var deleted = await store.DeleteAlertAsync(alertId, cancellationToken);
    return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
});

app.MapGet("/api/settings/discord", async Task<Ok<DashboardRuntimeSettings>> (
    DashboardSettingsStore settingsStore,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsStore.GetDashboardSettingsAsync(cancellationToken);
    return TypedResults.Ok(settings);
});

app.MapPut("/api/settings/discord", async Task<Ok<DashboardRuntimeSettings>> (
    SaveDashboardRuntimeSettingsRequest request,
    DashboardSettingsStore settingsStore,
    CancellationToken cancellationToken) =>
{
    var settings = await settingsStore.SaveDashboardSettingsAsync(request, cancellationToken);
    return TypedResults.Ok(settings);
});

app.MapGet("/api/dashboard/stream", async Task (HttpContext context, AlertDashboardStore store, DashboardStreamBroker broker, CancellationToken cancellationToken) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Append("X-Accel-Buffering", "no");

    var initialSnapshot = await store.GetSnapshotAsync(cancellationToken);
    await WriteSnapshotEventAsync(context, initialSnapshot, cancellationToken);

    await using var subscription = broker.Subscribe();

    while (!cancellationToken.IsCancellationRequested)
    {
        var readTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
        var heartbeatTask = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        var completedTask = await Task.WhenAny(readTask, heartbeatTask);

        if (completedTask == readTask)
        {
            var snapshot = await readTask;
            await WriteSnapshotEventAsync(context, snapshot, cancellationToken);
            continue;
        }

        await context.Response.WriteAsync(": ping\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
});

app.MapPost("/api/webhooks/tradingview", async Task<Results<Ok<WebhookAcceptedResponse>, BadRequest<string>, UnauthorizedHttpResult>> (
    HttpRequest request,
    TradingViewWebhookService service,
    IOptions<DashboardOptions> options,
    CancellationToken cancellationToken) =>
{
    var secret = options.Value.WebhookSecret?.Trim();
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var requestSecret = request.Headers["X-Webhook-Secret"].ToString();
        if (string.IsNullOrWhiteSpace(requestSecret))
        {
            requestSecret = request.Query["key"].ToString();
        }

        if (!string.Equals(secret, requestSecret, StringComparison.Ordinal))
        {
            return TypedResults.Unauthorized();
        }
    }

    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var rawPayload = await reader.ReadToEndAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(rawPayload))
    {
        return TypedResults.BadRequest("Webhook body is empty.");
    }

    var result = await service.ProcessAsync(rawPayload, cancellationToken);
    if (!result.Success)
    {
        return TypedResults.BadRequest(result.ErrorMessage ?? "Unable to process webhook payload.");
    }

    return TypedResults.Ok(new WebhookAcceptedResponse(
        result.Symbol ?? "Unknown",
        result.Lane ?? "Unknown",
        result.Timeframe ?? "Unknown",
        result.ForwardedToDiscord,
        result.ReceivedAtUtc));
});

static async Task WriteSnapshotEventAsync(HttpContext context, DashboardSnapshot snapshot, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(snapshot);
    await context.Response.WriteAsync($"event: snapshot\ndata: {json}\n\n", cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);
}

app.Run();

public partial class Program;
