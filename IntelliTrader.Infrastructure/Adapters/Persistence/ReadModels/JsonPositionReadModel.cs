using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

/// <summary>
/// Durable read-side projection for position queries.
/// </summary>
public sealed class JsonPositionReadModel : IPositionReadModel, IPositionReadModelProjectionWriter, IDisposable
{
    private static readonly string[] KnownQuoteCurrencies =
    [
        "USDT",
        "USDC",
        "BUSD",
        "TUSD",
        "FDUSD",
        "USD",
        "EUR",
        "BTC",
        "ETH",
        "BNB",
        "TRY",
        "GBP"
    ];

    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private ConcurrentDictionary<string, PositionReadModelDto> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonPositionReadModel(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        _filePath = filePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<PositionReadModelEntry?> GetByIdAsync(
        PositionId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.TryGetValue(id.ToString(), out var dto) ? MapEntry(dto) : null;
    }

    public async Task<PositionReadModelEntry?> GetByPairAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.Values
            .Where(dto => !dto.IsClosed)
            .Where(dto => dto.Pair.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.OpenedAt)
            .Select(MapEntry)
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<PositionReadModelEntry>> GetActiveAsync(
        string? market,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.Values
            .Where(dto => !dto.IsClosed)
            .Where(dto => string.IsNullOrWhiteSpace(market) ||
                          dto.QuoteCurrency.Equals(market, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.OpenedAt)
            .Select(MapEntry)
            .ToList();
    }

    public async Task<IReadOnlyList<PositionReadModelEntry>> GetClosedAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        TradingPair? pair,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        var safeLimit = limit <= 0 ? 100 : limit;

        return _cache.Values
            .Where(dto => dto.IsClosed)
            .Where(dto => dto.ClosedAt is not null && dto.ClosedAt >= from && dto.ClosedAt <= to)
            .Where(dto => pair is null || dto.Pair.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.ClosedAt ?? dto.OpenedAt)
            .Take(safeLimit)
            .Select(MapEntry)
            .ToList();
    }

    public Task ProjectAsync(PositionOpened domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var id = domainEvent.PositionId.ToString();
            var dto = cache.GetValueOrDefault(id) ?? CreateDto(
                domainEvent.PositionId,
                domainEvent.Pair,
                domainEvent.OccurredAt);

            dto.Pair = domainEvent.Pair.Symbol;
            dto.QuoteCurrency = domainEvent.Pair.QuoteCurrency;
            dto.AveragePrice = domainEvent.Price.Value;
            dto.TotalQuantity = domainEvent.Quantity.Value;
            dto.TotalCost = domainEvent.Cost.Amount;
            dto.CostCurrency = domainEvent.Cost.Currency;
            dto.TotalFees = domainEvent.Fees.Amount;
            dto.FeesCurrency = domainEvent.Fees.Currency;
            dto.DCALevel = 0;
            dto.OpenedAt = domainEvent.OccurredAt;
            dto.SignalRule = domainEvent.SignalRule;
            dto.IsClosed = false;
            dto.ClosedAt = null;
            AddEntryOrderId(dto, domainEvent.OrderId.Value);
            dto.EntryCount = ResolveEntryCount(dto);
            dto.UpdatedAt = domainEvent.OccurredAt;

            cache[id] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(DCAExecuted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var id = domainEvent.PositionId.ToString();
            var dto = cache.GetValueOrDefault(id) ?? CreateDto(
                domainEvent.PositionId,
                domainEvent.Pair,
                domainEvent.OccurredAt);

            dto.Pair = domainEvent.Pair.Symbol;
            dto.QuoteCurrency = domainEvent.Pair.QuoteCurrency;
            dto.AveragePrice = domainEvent.NewAveragePrice.Value;
            dto.TotalQuantity = domainEvent.NewTotalQuantity.Value;
            dto.TotalCost = domainEvent.NewTotalCost.Amount;
            dto.CostCurrency = domainEvent.NewTotalCost.Currency;
            dto.TotalFees += domainEvent.Fees.Amount;
            dto.FeesCurrency = domainEvent.Fees.Currency;
            dto.DCALevel = domainEvent.NewDCALevel;
            AddEntryOrderId(dto, domainEvent.OrderId.Value);
            dto.EntryCount = Math.Max(ResolveEntryCount(dto), domainEvent.NewDCALevel + 1);
            dto.UpdatedAt = domainEvent.OccurredAt;

            cache[id] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(PositionPartiallyClosed domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var id = domainEvent.PositionId.ToString();
            var dto = cache.GetValueOrDefault(id) ?? CreateDto(
                domainEvent.PositionId,
                domainEvent.Pair,
                domainEvent.OccurredAt);

            var previousCost = dto.TotalCost;
            var previousFees = dto.TotalFees;
            var releasedFeeRatio = previousCost == 0m
                ? 0m
                : Math.Clamp(domainEvent.ReleasedCost.Amount / previousCost, 0m, 1m);

            dto.Pair = domainEvent.Pair.Symbol;
            dto.QuoteCurrency = domainEvent.Pair.QuoteCurrency;
            dto.TotalQuantity = domainEvent.RemainingQuantity.Value;
            dto.TotalCost = domainEvent.RemainingCost?.Amount
                ?? Math.Max(0m, previousCost - domainEvent.ReleasedCost.Amount);
            dto.TotalFees = domainEvent.RemainingFees?.Amount
                ?? Math.Max(0m, previousFees - previousFees * releasedFeeRatio);
            dto.CostCurrency = domainEvent.RemainingCost?.Currency ?? domainEvent.ReleasedCost.Currency;
            dto.FeesCurrency = domainEvent.RemainingFees?.Currency ?? dto.FeesCurrency;
            dto.AveragePrice = domainEvent.NewAveragePrice?.Value
                ?? (dto.TotalQuantity == 0m ? dto.AveragePrice : dto.TotalCost / dto.TotalQuantity);
            dto.EntryCount = domainEvent.RemainingEntryCount ?? dto.EntryCount;
            dto.IsClosed = false;
            dto.ClosedAt = null;
            dto.UpdatedAt = domainEvent.OccurredAt;

            cache[id] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(PositionClosed domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var id = domainEvent.PositionId.ToString();
            var dto = cache.GetValueOrDefault(id) ?? CreateDto(
                domainEvent.PositionId,
                domainEvent.Pair,
                domainEvent.OccurredAt - domainEvent.Duration);

            dto.Pair = domainEvent.Pair.Symbol;
            dto.QuoteCurrency = domainEvent.Pair.QuoteCurrency;
            dto.TotalQuantity = domainEvent.SoldQuantity.Value;
            dto.TotalCost = domainEvent.TotalCost.Amount;
            dto.CostCurrency = domainEvent.TotalCost.Currency;
            dto.TotalFees = domainEvent.TotalFees.Amount;
            dto.FeesCurrency = domainEvent.TotalFees.Currency;
            dto.AveragePrice = domainEvent.SoldQuantity.IsZero
                ? dto.AveragePrice
                : domainEvent.TotalCost.Amount / domainEvent.SoldQuantity.Value;
            dto.DCALevel = domainEvent.DCALevel;
            dto.IsClosed = true;
            dto.ClosedAt = domainEvent.OccurredAt;
            dto.RealizedPnL = domainEvent.Proceeds.Amount - domainEvent.TotalCost.Amount - domainEvent.TotalFees.Amount;
            dto.RealizedPnLCurrency = domainEvent.Proceeds.Currency;
            dto.UpdatedAt = domainEvent.OccurredAt;

            cache[id] = dto;
        }, cancellationToken);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cacheLoaded = false;
            await LoadCacheUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task MutateAsync(
        Action<ConcurrentDictionary<string, PositionReadModelDto>> mutate,
        CancellationToken cancellationToken)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            mutate(_cache);
            await PersistCacheUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cacheLoaded)
        {
            return;
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cacheLoaded)
            {
                await LoadCacheUnlockedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task LoadCacheUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            _cache = new ConcurrentDictionary<string, PositionReadModelDto>(StringComparer.OrdinalIgnoreCase);
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new ConcurrentDictionary<string, PositionReadModelDto>(StringComparer.OrdinalIgnoreCase);
            _cacheLoaded = true;
            return;
        }

        var dtos = JsonSerializer.Deserialize<List<PositionReadModelDto>>(json, _jsonOptions)
                   ?? new List<PositionReadModelDto>();
        _cache = new ConcurrentDictionary<string, PositionReadModelDto>(
            dtos.ToDictionary(dto => dto.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        _cacheLoaded = true;
    }

    private async Task PersistCacheUnlockedAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dtos = _cache.Values
            .OrderByDescending(dto => dto.UpdatedAt == default ? dto.OpenedAt : dto.UpdatedAt)
            .ToList();
        var json = JsonSerializer.Serialize(dtos, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static PositionReadModelDto CreateDto(
        PositionId positionId,
        TradingPair pair,
        DateTimeOffset openedAt)
    {
        return new PositionReadModelDto
        {
            Id = positionId.ToString(),
            Pair = pair.Symbol,
            QuoteCurrency = pair.QuoteCurrency,
            CostCurrency = pair.QuoteCurrency,
            FeesCurrency = pair.QuoteCurrency,
            OpenedAt = openedAt,
            UpdatedAt = openedAt
        };
    }

    private static PositionReadModelEntry MapEntry(PositionReadModelDto dto)
    {
        var pair = TradingPair.Create(dto.Pair, ResolveQuoteCurrency(dto.Pair, dto.QuoteCurrency));
        var costCurrency = ResolveCurrency(dto.CostCurrency, pair.QuoteCurrency);
        var feesCurrency = ResolveCurrency(dto.FeesCurrency, pair.QuoteCurrency);

        return new PositionReadModelEntry
        {
            Id = PositionId.From(dto.Id),
            Pair = pair,
            AveragePrice = Price.Create(dto.AveragePrice),
            TotalQuantity = Quantity.Create(dto.TotalQuantity),
            TotalCost = Money.Create(dto.TotalCost, costCurrency),
            TotalFees = Money.Create(dto.TotalFees, feesCurrency),
            DCALevel = dto.DCALevel,
            EntryCount = ResolveEntryCount(dto),
            OpenedAt = dto.OpenedAt,
            SignalRule = dto.SignalRule,
            IsClosed = dto.IsClosed,
            ClosedAt = dto.ClosedAt,
            RealizedPnL = dto.RealizedPnL.HasValue
                ? Money.Create(dto.RealizedPnL.Value, ResolveCurrency(dto.RealizedPnLCurrency, pair.QuoteCurrency))
                : null
        };
    }

    private static void AddEntryOrderId(PositionReadModelDto dto, string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        if (!dto.EntryOrderIds.Contains(orderId, StringComparer.OrdinalIgnoreCase))
        {
            dto.EntryOrderIds.Add(orderId);
        }
    }

    private static int ResolveEntryCount(PositionReadModelDto dto)
    {
        return dto.EntryCount > 0 ? dto.EntryCount : dto.EntryOrderIds.Count;
    }

    private static string ResolveCurrency(string? currency, string fallback)
    {
        return string.IsNullOrWhiteSpace(currency) ? fallback : currency.ToUpperInvariant();
    }

    private static string ResolveQuoteCurrency(string pair, string? explicitQuoteCurrency)
    {
        if (!string.IsNullOrWhiteSpace(explicitQuoteCurrency))
        {
            return explicitQuoteCurrency.ToUpperInvariant();
        }

        var symbol = pair.ToUpperInvariant();
        var knownQuote = KnownQuoteCurrencies.FirstOrDefault(quote =>
            symbol.EndsWith(quote, StringComparison.Ordinal) && symbol.Length > quote.Length);
        if (knownQuote is not null)
        {
            return knownQuote;
        }

        return symbol.Length > 3 ? symbol[^3..] : symbol;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _fileLock.Dispose();
        _disposed = true;
    }

    private sealed class PositionReadModelDto
    {
        public string Id { get; set; } = string.Empty;
        public string Pair { get; set; } = string.Empty;
        public string QuoteCurrency { get; set; } = string.Empty;
        public decimal AveragePrice { get; set; }
        public decimal TotalQuantity { get; set; }
        public decimal TotalCost { get; set; }
        public string CostCurrency { get; set; } = string.Empty;
        public decimal TotalFees { get; set; }
        public string FeesCurrency { get; set; } = string.Empty;
        public int DCALevel { get; set; }
        public int EntryCount { get; set; }
        public List<string> EntryOrderIds { get; set; } = new();
        public DateTimeOffset OpenedAt { get; set; }
        public string? SignalRule { get; set; }
        public bool IsClosed { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public decimal? RealizedPnL { get; set; }
        public string? RealizedPnLCurrency { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
