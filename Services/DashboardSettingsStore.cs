using System.Text.Json;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class DashboardSettingsStore
{
    private readonly DashboardOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private DashboardRuntimeSettings? _cachedSettings;

    public DashboardSettingsStore(
        Microsoft.Extensions.Options.IOptions<DashboardOptions> options,
        IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<DashboardRuntimeSettings> GetDashboardSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _cachedSettings!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DashboardRuntimeSettings> SaveDashboardSettingsAsync(
        SaveDashboardRuntimeSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            _cachedSettings = new DashboardRuntimeSettings(
                Normalize(request.DuWebhookUrl),
                Normalize(request.DdWebhookUrl),
                request.DailyResetEnabled,
                _cachedSettings?.LastAutoResetLocalDate);

            await PersistAsync(cancellationToken);
            return _cachedSettings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkDailyResetCompletedAsync(DateOnly localDate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            _cachedSettings = _cachedSettings! with
            {
                LastAutoResetLocalDate = localDate.ToString("yyyy-MM-dd")
            };

            await PersistAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cachedSettings is not null)
        {
            return;
        }

        var filePath = GetSettingsFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            _cachedSettings = new DashboardRuntimeSettings(
                Normalize(_options.ForwardDiscordDuWebhookUrl),
                Normalize(_options.ForwardDiscordDdWebhookUrl),
                _options.DailyResetEnabledByDefault);
            await PersistAsync(cancellationToken);
            return;
        }

        await using var stream = File.OpenRead(filePath);
        _cachedSettings = await JsonSerializer.DeserializeAsync<DashboardRuntimeSettings>(stream, _jsonOptions, cancellationToken)
            ?? new DashboardRuntimeSettings(
                Normalize(_options.ForwardDiscordDuWebhookUrl),
                Normalize(_options.ForwardDiscordDdWebhookUrl),
                _options.DailyResetEnabledByDefault);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var filePath = GetSettingsFilePath();
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, _cachedSettings, _jsonOptions, cancellationToken);
    }

    private string GetSettingsFilePath()
    {
        return Path.IsPathRooted(_options.RuntimeSettingsPath)
            ? _options.RuntimeSettingsPath
            : Path.Combine(_hostEnvironment.ContentRootPath, _options.RuntimeSettingsPath);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
