using System.Text.Json;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class AlertDashboardStore
{
    private readonly DashboardOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly DashboardStreamBroker _streamBroker;
    private readonly ILogger<AlertDashboardStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private DashboardState? _state;

    public AlertDashboardStore(
        Microsoft.Extensions.Options.IOptions<DashboardOptions> options,
        IHostEnvironment hostEnvironment,
        DashboardStreamBroker streamBroker,
        ILogger<AlertDashboardStore> logger)
    {
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _streamBroker = streamBroker;
        _logger = logger;
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _state!.ToSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RegisterAlertAsync(ParsedTradingViewAlert alert, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var timeframes = _options.GetConfiguredTimeframes();

            var symbolState = _state!.Symbols.SingleOrDefault(x => string.Equals(x.Symbol, alert.Symbol, StringComparison.OrdinalIgnoreCase));
            if (symbolState is null)
            {
                symbolState = DashboardState.CreateSymbol(alert.Symbol, timeframes);
                _state.Symbols.Add(symbolState);
            }

            var folder = string.Equals(alert.Lane, "DU", StringComparison.OrdinalIgnoreCase)
                ? symbolState.SduFolder
                : symbolState.SddFolder;

            var slot = folder.Slots.SingleOrDefault(x => string.Equals(x.Timeframe, alert.Timeframe, StringComparison.OrdinalIgnoreCase));
            if (slot is null)
            {
                slot = new TimeframeSlotState { Timeframe = alert.Timeframe };
                folder.Slots.Add(slot);
                folder.Slots = folder.Slots
                    .OrderBy(x => OrderTimeframe(x.Timeframe, timeframes))
                    .ThenBy(x => x.Timeframe, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            slot.IsTriggered = true;
            slot.Title = alert.Title;
            slot.Price = alert.Price;
            slot.TriggeredAtText = alert.TriggeredAtText;
            slot.LastTriggeredAtUtc = alert.ReceivedAtUtc;

            folder.RecentAlerts.Insert(0, new AlertCardState
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = alert.Title,
                Description = alert.Description,
                Timeframe = alert.Timeframe,
                TriggeredAtText = alert.TriggeredAtText,
                ReceivedAtUtc = alert.ReceivedAtUtc,
                Price = alert.Price,
                FooterText = alert.FooterText
            });

            if (folder.RecentAlerts.Count > _options.MaxAlertsPerFolder)
            {
                folder.RecentAlerts.RemoveRange(_options.MaxAlertsPerFolder, folder.RecentAlerts.Count - _options.MaxAlertsPerFolder);
            }

            symbolState.LastAlertAtUtc = alert.ReceivedAtUtc;
            _state.UpdatedAtUtc = alert.ReceivedAtUtc;
            _state.Symbols = _state.Symbols
                .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await PersistAsync(cancellationToken);
            _streamBroker.Publish(_state.ToSnapshot());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAlertAsync(string alertId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            var timeframes = _options.GetConfiguredTimeframes();
            SymbolState? affectedSymbol = null;
            var deleted = false;

            foreach (var symbol in _state!.Symbols)
            {
                var removedFromSdu = symbol.SduFolder.RemoveAlert(alertId, timeframes);
                var removedFromSdd = symbol.SddFolder.RemoveAlert(alertId, timeframes);
                if (!removedFromSdu && !removedFromSdd)
                {
                    continue;
                }

                affectedSymbol = symbol;
                deleted = true;
                break;
            }

            if (!deleted || affectedSymbol is null)
            {
                return false;
            }

            affectedSymbol.LastAlertAtUtc = affectedSymbol.GetLatestAlertAtUtc();

            if (!affectedSymbol.SduFolder.RecentAlerts.Any() && !affectedSymbol.SddFolder.RecentAlerts.Any())
            {
                _state.Symbols.Remove(affectedSymbol);
            }

            _state.UpdatedAtUtc = _state.Symbols
                .Select(symbol => symbol.GetLatestAlertAtUtc())
                .Where(timestamp => timestamp.HasValue)
                .Select(timestamp => timestamp!.Value)
                .DefaultIfEmpty(DateTimeOffset.UtcNow)
                .Max();

            _state.Symbols = _state.Symbols
                .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await PersistAsync(cancellationToken);
            _streamBroker.Publish(_state.ToSnapshot());
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetDashboardAsync(DateTimeOffset resetAtUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _state!.Symbols.Clear();
            _state.UpdatedAtUtc = resetAtUtc;

            await PersistAsync(cancellationToken);
            _streamBroker.Publish(_state.ToSnapshot());
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_state is not null)
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
            _state = DashboardState.CreateEmpty();
            await PersistAsync(cancellationToken);
            return;
        }

        await using var stream = File.OpenRead(fullPath);
        _state = await JsonSerializer.DeserializeAsync<DashboardState>(stream, _jsonOptions, cancellationToken)
            ?? DashboardState.CreateEmpty();

        _state.Normalize(_options.GetConfiguredTimeframes());
        _logger.LogInformation("Loaded dashboard state for {Count} symbols.", _state.Symbols.Count);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var fullPath = GetStorageFullPath();
        await using var stream = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(stream, _state, _jsonOptions, cancellationToken);
    }

    private string GetStorageFullPath()
    {
        return Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(_hostEnvironment.ContentRootPath, _options.StoragePath);
    }

    private sealed class DashboardState
    {
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public List<SymbolState> Symbols { get; set; } = [];

        public static DashboardState CreateEmpty()
        {
            return new DashboardState();
        }

        public static SymbolState CreateSymbol(string symbol, IReadOnlyList<string> timeframes)
        {
            return new SymbolState
            {
                Symbol = symbol,
                SduFolder = FolderState.Create("DU", "SDU 卡夾", "所有 DU 鬧鐘都進這個卡夾。", timeframes),
                SddFolder = FolderState.Create("DD", "SDD 卡夾", "所有 DD 鬧鐘都進這個卡夾。", timeframes)
            };
        }

        public void Normalize(IReadOnlyList<string> timeframes)
        {
            foreach (var symbol in Symbols)
            {
                symbol.SduFolder ??= FolderState.Create("DU", "SDU 卡夾", "所有 DU 鬧鐘都進這個卡夾。", timeframes);
                symbol.SddFolder ??= FolderState.Create("DD", "SDD 卡夾", "所有 DD 鬧鐘都進這個卡夾。", timeframes);
                symbol.SduFolder.Normalize(timeframes);
                symbol.SddFolder.Normalize(timeframes);
            }
        }

        public DashboardSnapshot ToSnapshot()
        {
            return new DashboardSnapshot(
                UpdatedAtUtc.ToString("O"),
                FormatTime(UpdatedAtUtc),
                Symbols.Sum(x => x.SduFolder.RecentAlerts.Count + x.SddFolder.RecentAlerts.Count),
                Symbols
                    .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                    .Select(symbol => new SymbolDashboardSnapshot(
                        symbol.Symbol,
                        symbol.LastAlertAtUtc?.ToString("O"),
                        symbol.LastAlertAtUtc.HasValue ? FormatTime(symbol.LastAlertAtUtc.Value) : "尚未收到",
                        symbol.SduFolder.ToSnapshot(),
                        symbol.SddFolder.ToSnapshot()))
                    .ToList());
        }
    }

    private sealed class SymbolState
    {
        public string Symbol { get; set; } = string.Empty;

        public DateTimeOffset? LastAlertAtUtc { get; set; }

        public FolderState SduFolder { get; set; } = null!;

        public FolderState SddFolder { get; set; } = null!;

        public DateTimeOffset? GetLatestAlertAtUtc()
        {
            var timestamps = new[] { SduFolder.GetLatestAlertAtUtc(), SddFolder.GetLatestAlertAtUtc() }
                .Where(timestamp => timestamp.HasValue)
                .Select(timestamp => timestamp!.Value)
                .ToList();

            return timestamps.Count > 0 ? timestamps.Max() : null;
        }
    }

    private sealed class FolderState
    {
        public string Lane { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public List<TimeframeSlotState> Slots { get; set; } = [];

        public List<AlertCardState> RecentAlerts { get; set; } = [];

        public static FolderState Create(string lane, string displayName, string description, IReadOnlyList<string> timeframes)
        {
            return new FolderState
            {
                Lane = lane,
                DisplayName = displayName,
                Description = description,
                Slots = timeframes.Select(x => new TimeframeSlotState { Timeframe = x }).ToList(),
                RecentAlerts = []
            };
        }

        public void Normalize(IReadOnlyList<string> timeframes)
        {
            foreach (var timeframe in timeframes)
            {
                if (!Slots.Any(x => string.Equals(x.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase)))
                {
                    Slots.Add(new TimeframeSlotState { Timeframe = timeframe });
                }
            }

            Slots = Slots
                .OrderBy(x => OrderTimeframe(x.Timeframe, timeframes))
                .ThenBy(x => x.Timeframe, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool RemoveAlert(string alertId, IReadOnlyList<string> timeframes)
        {
            var removedCount = RecentAlerts.RemoveAll(alert => string.Equals(alert.Id, alertId, StringComparison.OrdinalIgnoreCase));
            if (removedCount == 0)
            {
                return false;
            }

            RebuildSlots(timeframes);
            return true;
        }

        public FolderSnapshot ToSnapshot()
        {
            var triggeredCount = Slots.Count(x => x.IsTriggered);
            return new FolderSnapshot(
                Lane,
                DisplayName,
                Description,
                $"{triggeredCount} 已亮燈",
                Slots.Select(slot => new TimeframeSlotSnapshot(
                    slot.Timeframe,
                    slot.IsTriggered,
                    slot.TriggeredAtText,
                    slot.LastTriggeredAtUtc?.ToString("O"),
                    slot.LastTriggeredAtUtc.HasValue ? FormatTime(slot.LastTriggeredAtUtc.Value) : null,
                    slot.Price,
                    slot.Title))
                .ToList(),
                RecentAlerts
                    .OrderByDescending(x => x.ReceivedAtUtc)
                    .Select(alert => new AlertCardSnapshot(
                        alert.Id,
                        alert.Title,
                        alert.Description,
                        alert.Timeframe,
                        alert.Price,
                        alert.FooterText,
                        alert.TriggeredAtText,
                        alert.ReceivedAtUtc.ToString("O"),
                        FormatTime(alert.ReceivedAtUtc)))
                    .ToList());
        }

        public DateTimeOffset? GetLatestAlertAtUtc()
        {
            return RecentAlerts.Count > 0 ? RecentAlerts.Max(alert => alert.ReceivedAtUtc) : null;
        }

        private void RebuildSlots(IReadOnlyList<string> timeframes)
        {
            var allTimeframes = timeframes
                .Concat(RecentAlerts.Select(alert => alert.Timeframe))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(timeframe => OrderTimeframe(timeframe, timeframes))
                .ThenBy(timeframe => timeframe, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Slots = allTimeframes.Select(timeframe =>
            {
                var latestAlert = RecentAlerts
                    .Where(alert => string.Equals(alert.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(alert => alert.ReceivedAtUtc)
                    .FirstOrDefault();

                return latestAlert is null
                    ? new TimeframeSlotState
                    {
                        Timeframe = timeframe,
                        IsTriggered = false
                    }
                    : new TimeframeSlotState
                    {
                        Timeframe = timeframe,
                        IsTriggered = true,
                        TriggeredAtText = latestAlert.TriggeredAtText,
                        LastTriggeredAtUtc = latestAlert.ReceivedAtUtc,
                        Price = latestAlert.Price,
                        Title = latestAlert.Title
                    };
            }).ToList();
        }
    }

    private sealed class TimeframeSlotState
    {
        public string Timeframe { get; set; } = string.Empty;

        public bool IsTriggered { get; set; }

        public string? TriggeredAtText { get; set; }

        public DateTimeOffset? LastTriggeredAtUtc { get; set; }

        public string? Price { get; set; }

        public string? Title { get; set; }
    }

    private sealed class AlertCardState
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Timeframe { get; set; } = string.Empty;

        public string TriggeredAtText { get; set; } = string.Empty;

        public DateTimeOffset ReceivedAtUtc { get; set; }

        public string? Price { get; set; }

        public string? FooterText { get; set; }
    }

    private static int OrderTimeframe(string timeframe, IReadOnlyList<string> configuredTimeframes)
    {
        var index = configuredTimeframes
            .Select((value, idx) => new { value, idx })
            .FirstOrDefault(x => string.Equals(x.value, timeframe, StringComparison.OrdinalIgnoreCase));

        return index?.idx ?? int.MaxValue;
    }

    private static string FormatTime(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
