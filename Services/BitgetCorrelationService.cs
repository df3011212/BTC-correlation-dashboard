using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingViewWebhookDashboard.Models;

namespace TradingViewWebhookDashboard.Services;

public sealed class BitgetCorrelationService
{
    public const string HttpClientName = "bitget-correlation";

    private static readonly TimeSpan PerRequestDelay = TimeSpan.FromMilliseconds(75);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CorrelationDashboardOptions _options;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<BitgetCorrelationService> _logger;

    public BitgetCorrelationService(
        IHttpClientFactory httpClientFactory,
        IOptions<CorrelationDashboardOptions> options,
        ILogger<BitgetCorrelationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeZone = _options.GetDisplayTimeZone();
        _logger = logger;
    }

    public async Task<CorrelationDashboardSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var symbols = await GetTradableSymbolsAsync(client, cancellationToken);
        if (!symbols.Contains(_options.BaseSymbol, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Bitget contracts list does not include base symbol '{_options.BaseSymbol}'.");
        }

        var baseSeries = await GetCloseSeriesAsync(client, _options.BaseSymbol, cancellationToken);
        var requiredCandleCount = _options.GetSafeCandleLimit();
        if (baseSeries.Count < requiredCandleCount)
        {
            throw new InvalidOperationException($"Base symbol '{_options.BaseSymbol}' returned only {baseSeries.Count} candles.");
        }

        var baseCloses = baseSeries.Select(point => point.Close).ToList();
        var baseLastClose = baseCloses[^1];
        var basePriceChangePercent = CalculatePriceChangePercent(baseCloses);
        var (btcDirection, btcDirectionLabel, directionRuleText) = DescribeBaseDirection(basePriceChangePercent);

        var candidateSymbols = symbols
            .Where(symbol => !string.Equals(symbol, _options.BaseSymbol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new ConcurrentBag<CorrelationCoinResult>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _options.GetSafeMaxParallelRequests()
        };

        await Parallel.ForEachAsync(candidateSymbols, parallelOptions, async (symbol, token) =>
        {
            try
            {
                var closeSeries = await GetCloseSeriesAsync(client, symbol, token);
                if (closeSeries.Count != baseSeries.Count)
                {
                    return;
                }

                var closes = closeSeries.Select(point => point.Close).ToList();
                var correlation = CorrelationMath.CalculatePearson(baseCloses, closes);
                if (double.IsNaN(correlation) || correlation < _options.MinCorrelation || correlation > _options.MaxCorrelation)
                {
                    return;
                }

                var priceChangePercent = CalculatePriceChangePercent(closes);
                var directionAligned = IsDirectionAligned(basePriceChangePercent, priceChangePercent);
                if (!directionAligned)
                {
                    return;
                }

                results.Add(new CorrelationCoinResult
                {
                    Symbol = symbol,
                    TradingViewSymbol = $"{symbol}.P",
                    Correlation = Math.Round(correlation, 4),
                    CorrelationText = correlation.ToString("0.0000", CultureInfo.InvariantCulture),
                    PriceChangePercent = priceChangePercent,
                    PriceChangeText = FormatPercent(priceChangePercent),
                    LatestClose = closes[^1],
                    LatestCloseText = closes[^1].ToString("0.########", CultureInfo.InvariantCulture),
                    DirectionAligned = true,
                    DirectionLabel = "同方向"
                });

                await Task.Delay(PerRequestDelay, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping symbol {Symbol} because Bitget data could not be processed.", symbol);
            }
        });

        var orderedResults = results
            .OrderByDescending(item => item.Correlation)
            .ThenByDescending(item => ApplyDirectionWeight(item.PriceChangePercent, basePriceChangePercent))
            .ThenBy(item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(_options.GetSafeTopResultCount())
            .ToList();

        return CorrelationDashboardSnapshotFactory.CreateSuccess(
            _options,
            _timeZone,
            DateTimeOffset.UtcNow,
            baseLastClose,
            basePriceChangePercent,
            btcDirection,
            btcDirectionLabel,
            directionRuleText,
            candidateSymbols.Count,
            orderedResults);
    }

    private async Task<List<string>> GetTradableSymbolsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"api/v2/mix/market/contracts?productType={Uri.EscapeDataString(_options.ProductType)}",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<BitgetEnvelope<List<BitgetContractDto>>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Bitget contracts response was empty.");

        if (!string.Equals(envelope.Code, "00000", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bitget contracts request failed: {envelope.Msg}");
        }

        return envelope.Data
            .Where(contract =>
                string.Equals(contract.SymbolType, "perpetual", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(contract.SymbolStatus, "normal", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(contract.QuoteCoin, "USDT", StringComparison.OrdinalIgnoreCase) &&
                IsSupportedSymbol(contract.Symbol))
            .Select(contract => contract.Symbol.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<CandleClosePoint>> GetCloseSeriesAsync(HttpClient client, string symbol, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"api/v2/mix/market/candles?symbol={Uri.EscapeDataString(symbol)}&productType={Uri.EscapeDataString(_options.ProductType)}&granularity={Uri.EscapeDataString(_options.Granularity)}&limit={_options.GetSafeCandleLimit()}",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("code").GetString(), "00000", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Bitget candles request failed for {symbol}: {root.GetProperty("msg").GetString()}");
        }

        var closes = new List<CandleClosePoint>();
        foreach (var candle in root.GetProperty("data").EnumerateArray())
        {
            if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() < 5)
            {
                continue;
            }

            var openTimeRaw = candle[0].GetString();
            var closeRaw = candle[4].GetString();
            if (!long.TryParse(openTimeRaw, out var openTimeUnixMs) || !decimal.TryParse(closeRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var close))
            {
                continue;
            }

            closes.Add(new CandleClosePoint(openTimeUnixMs, close));
        }

        return closes
            .OrderBy(point => point.OpenTimeUnixMs)
            .TakeLast(_options.GetSafeCandleLimit())
            .ToList();
    }

    private static bool IsSupportedSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return symbol.All(ch => char.IsAsciiLetterOrDigit(ch));
    }

    private static decimal CalculatePriceChangePercent(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 2 || closes[^2] == 0m)
        {
            return 0m;
        }

        return ((closes[^1] - closes[^2]) / closes[^2]) * 100m;
    }

    private static (string Direction, string Label, string RuleText) DescribeBaseDirection(decimal basePriceChangePercent)
    {
        return basePriceChangePercent switch
        {
            > 0m => (
                "up",
                "BTC 最新 15 分鐘上漲",
                "只保留和 BTC 最新 15 分鐘同方向上漲，且相關係數介於 0.70 到 1.00 的標的。"),
            < 0m => (
                "down",
                "BTC 最新 15 分鐘下跌",
                "目前 BTC 在下跌，因此列表改為保留和 BTC 最新 15 分鐘同方向下跌，且相關係數介於 0.70 到 1.00 的標的。"),
            _ => (
                "flat",
                "BTC 最新 15 分鐘持平",
                "BTC 最新 15 分鐘幾乎持平，因此本次以相關係數 0.70 到 1.00 為主，不額外限制方向。")
        };
    }

    private static bool IsDirectionAligned(decimal baseChangePercent, decimal candidateChangePercent)
    {
        var baseSign = Math.Sign(baseChangePercent);
        if (baseSign == 0)
        {
            return true;
        }

        return Math.Sign(candidateChangePercent) == baseSign;
    }

    private static decimal ApplyDirectionWeight(decimal candidateChangePercent, decimal baseChangePercent)
    {
        return Math.Sign(baseChangePercent) >= 0
            ? candidateChangePercent
            : candidateChangePercent * -1m;
    }

    private static string FormatPercent(decimal value)
    {
        return value switch
        {
            > 0m => $"+{value:0.00}%",
            < 0m => $"{value:0.00}%",
            _ => "0.00%"
        };
    }

    private sealed record BitgetEnvelope<T>(string Code, string Msg, T Data);

    private sealed record BitgetContractDto(
        string Symbol,
        string QuoteCoin,
        string SymbolType,
        string SymbolStatus);

    private sealed record CandleClosePoint(long OpenTimeUnixMs, decimal Close);
}
