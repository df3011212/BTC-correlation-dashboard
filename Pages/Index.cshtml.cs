using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using TradingViewWebhookDashboard.Models;
using TradingViewWebhookDashboard.Services;

namespace TradingViewWebhookDashboard.Pages;

public class IndexModel : PageModel
{
    private readonly CorrelationDashboardStore _store;
    private readonly CorrelationDashboardOptions _options;

    public IndexModel(
        CorrelationDashboardStore store,
        IOptions<CorrelationDashboardOptions> options)
    {
        _store = store;
        _options = options.Value;
    }

    public CorrelationDashboardSnapshot InitialSnapshot { get; private set; } = new();

    public string InitialSnapshotJson { get; private set; } = "{}";

    public string PageTitle => _options.PageTitle;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        InitialSnapshot = await _store.GetSnapshotAsync(cancellationToken);
        InitialSnapshotJson = JsonSerializer.Serialize(InitialSnapshot, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
