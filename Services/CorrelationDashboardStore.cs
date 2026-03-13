using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class CorrelationDashboardStore
{
    private readonly CorrelationDashboardOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<CorrelationDashboardStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private CorrelationDashboardSnapshot? _snapshot;

    public CorrelationDashboardStore(
        IOptions<CorrelationDashboardOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<CorrelationDashboardStore> logger)
    {
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task<CorrelationDashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _snapshot!;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveSnapshotAsync(CorrelationDashboardSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _snapshot = snapshot;
            await PersistAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordRefreshFailureAsync(string errorMessage, DateTimeOffset attemptedAtUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _snapshot = CorrelationDashboardSnapshotFactory.CreateRefreshFailure(
                _snapshot!,
                _options,
                _options.GetDisplayTimeZone(),
                attemptedAtUtc,
                errorMessage);

            await PersistAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is not null)
        {
            return;
        }

        var fullPath = GetStorageFullPath();
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(fullPath))
        {
            _snapshot = CorrelationDashboardSnapshotFactory.CreateEmpty(_options, _options.GetDisplayTimeZone());
            await PersistAsync(cancellationToken);
            return;
        }

        await using var stream = File.OpenRead(fullPath);
        _snapshot = await JsonSerializer.DeserializeAsync<CorrelationDashboardSnapshot>(stream, _jsonOptions, cancellationToken)
            ?? CorrelationDashboardSnapshotFactory.CreateEmpty(_options, _options.GetDisplayTimeZone());

        _logger.LogInformation("Loaded correlation dashboard snapshot with {Count} results.", _snapshot.Results.Count);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var fullPath = GetStorageFullPath();
        await using var stream = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(stream, _snapshot, _jsonOptions, cancellationToken);
    }

    private string GetStorageFullPath()
    {
        return Path.IsPathRooted(_options.SnapshotPath)
            ? _options.SnapshotPath
            : Path.Combine(_hostEnvironment.ContentRootPath, _options.SnapshotPath);
    }
}
