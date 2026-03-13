namespace TradingViewWebhookDashboard.Models;

public sealed record CorrelationDashboardSnapshot
{
    public string Title { get; init; } = string.Empty;

    public string BaseSymbol { get; init; } = string.Empty;

    public string BaseTradingViewSymbol { get; init; } = string.Empty;

    public string MarketLabel { get; init; } = string.Empty;

    public string WindowLabel { get; init; } = string.Empty;

    public string CorrelationRangeLabel { get; init; } = string.Empty;

    public int RefreshIntervalMinutes { get; init; }

    public string UpdatedAtUtc { get; init; } = string.Empty;

    public string UpdatedAtLocalText { get; init; } = string.Empty;

    public string? LastSuccessfulRefreshUtc { get; init; }

    public string LastSuccessfulRefreshLocalText { get; init; } = string.Empty;

    public string NextScheduledRefreshLocalText { get; init; } = string.Empty;

    public string BtcDirection { get; init; } = "flat";

    public string BtcDirectionLabel { get; init; } = string.Empty;

    public decimal BasePriceChangePercent { get; init; }

    public string BasePriceChangeText { get; init; } = string.Empty;

    public decimal BaseLastClose { get; init; }

    public string BaseLastCloseText { get; init; } = string.Empty;

    public int TotalSymbolsScanned { get; init; }

    public int MatchedCount { get; init; }

    public string DirectionRuleText { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<CorrelationCoinResult> Results { get; init; } = [];
}

public sealed record CorrelationCoinResult
{
    public string Symbol { get; init; } = string.Empty;

    public string TradingViewSymbol { get; init; } = string.Empty;

    public double Correlation { get; init; }

    public string CorrelationText { get; init; } = string.Empty;

    public decimal PriceChangePercent { get; init; }

    public string PriceChangeText { get; init; } = string.Empty;

    public decimal LatestClose { get; init; }

    public string LatestCloseText { get; init; } = string.Empty;

    public bool DirectionAligned { get; init; }

    public string DirectionLabel { get; init; } = string.Empty;
}
