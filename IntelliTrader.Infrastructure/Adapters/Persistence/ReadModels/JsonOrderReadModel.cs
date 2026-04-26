using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

/// <summary>
/// Durable read-side projection for order queries.
/// </summary>
public sealed class JsonOrderReadModel : IOrderReadModel, IOrderReadModelProjectionWriter, IDisposable
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
    private ConcurrentDictionary<string, OrderReadModelDto> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonOrderReadModel(string filePath)
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

    public async Task<OrderView?> GetByIdAsync(
        OrderId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.TryGetValue(id.Value, out var dto) ? MapOrderView(dto) : null;
    }

    public async Task<IReadOnlyList<OrderView>> GetRecentAsync(
        TradingPair? pair,
        OrderLifecycleStatus? status,
        DomainOrderSide? side,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        var safeLimit = limit <= 0 ? 50 : limit;

        return _cache.Values
            .Where(dto => pair is null || dto.Pair.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(dto => status is null || ParseStatus(dto.Status) == status)
            .Where(dto => side is null || ParseSide(dto.Side) == side)
            .OrderByDescending(dto => dto.SubmittedAt)
            .Take(safeLimit)
            .Select(MapOrderView)
            .ToList();
    }

    public async Task<IReadOnlyList<OrderView>> GetActiveAsync(
        TradingPair? pair,
        DomainOrderSide? side,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        var safeLimit = limit <= 0 ? 50 : limit;

        return _cache.Values
            .Where(dto => !IsTerminal(ParseStatus(dto.Status)))
            .Where(dto => pair is null || dto.Pair.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(dto => side is null || ParseSide(dto.Side) == side)
            .OrderByDescending(dto => dto.SubmittedAt)
            .Take(safeLimit)
            .Select(MapOrderView)
            .ToList();
    }

    public async Task<IReadOnlyList<TradeHistoryEntry>> GetTradingHistoryAsync(
        TradingPair? pair,
        DateTimeOffset from,
        DateTimeOffset to,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        var safeOffset = Math.Max(0, offset);
        var safeLimit = limit <= 0 ? 100 : limit;

        return _cache.Values
            .Where(dto => dto.FilledQuantity > 0m)
            .Where(dto => !string.IsNullOrWhiteSpace(dto.RelatedPositionId))
            .Where(dto => dto.SubmittedAt >= from && dto.SubmittedAt <= to)
            .Where(dto => pair is null || dto.Pair.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dto => dto.SubmittedAt)
            .Skip(safeOffset)
            .Take(safeLimit)
            .Select(MapTradeHistoryEntry)
            .ToList();
    }

    public Task ProjectAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var quoteCurrency = ResolveQuoteCurrency(domainEvent.Pair, domainEvent.QuoteCurrency);
            var dto = cache.GetValueOrDefault(domainEvent.OrderId) ?? new OrderReadModelDto
            {
                Id = domainEvent.OrderId,
                Pair = domainEvent.Pair.ToUpperInvariant(),
                QuoteCurrency = quoteCurrency,
                Side = domainEvent.Side.ToString(),
                Type = domainEvent.OrderType.ToString(),
                Status = OrderLifecycleStatus.Submitted.ToString(),
                CostCurrency = quoteCurrency,
                FeesCurrency = quoteCurrency,
                SubmittedAt = domainEvent.OccurredAt
            };

            dto.Pair = domainEvent.Pair.ToUpperInvariant();
            dto.QuoteCurrency = quoteCurrency;
            dto.Side = domainEvent.Side.ToString();
            dto.Type = domainEvent.OrderType.ToString();
            dto.Status = string.IsNullOrWhiteSpace(dto.Status)
                ? OrderLifecycleStatus.Submitted.ToString()
                : dto.Status;
            dto.RequestedQuantity = domainEvent.Amount;
            dto.SubmittedPrice = domainEvent.Price;
            dto.SignalRule = domainEvent.SignalRule;
            dto.Intent = domainEvent.Intent;
            dto.RelatedPositionId = domainEvent.RelatedPositionId ?? dto.RelatedPositionId;
            dto.CostCurrency = quoteCurrency;
            dto.FeesCurrency = quoteCurrency;
            dto.SubmittedAt = dto.SubmittedAt == default ? domainEvent.OccurredAt : dto.SubmittedAt;

            cache[domainEvent.OrderId] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(OrderFilledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var quoteCurrency = ResolveQuoteCurrency(domainEvent.Pair, null);
            var dto = cache.GetValueOrDefault(domainEvent.OrderId) ?? new OrderReadModelDto
            {
                Id = domainEvent.OrderId,
                Pair = domainEvent.Pair.ToUpperInvariant(),
                QuoteCurrency = quoteCurrency,
                Side = domainEvent.Side.ToString(),
                Type = DomainOrderType.Market.ToString(),
                Status = OrderLifecycleStatus.Submitted.ToString(),
                RequestedQuantity = domainEvent.FilledAmount,
                SubmittedPrice = domainEvent.AveragePrice,
                CostCurrency = quoteCurrency,
                FeesCurrency = quoteCurrency,
                SubmittedAt = domainEvent.OccurredAt
            };

            var currentStatus = ParseStatus(dto.Status);
            dto.Pair = domainEvent.Pair.ToUpperInvariant();
            dto.QuoteCurrency = string.IsNullOrWhiteSpace(dto.QuoteCurrency) ? quoteCurrency : dto.QuoteCurrency;
            dto.Side = domainEvent.Side.ToString();
            dto.Status = currentStatus == OrderLifecycleStatus.Canceled
                ? OrderLifecycleStatus.Canceled.ToString()
                : domainEvent.IsPartialFill
                    ? OrderLifecycleStatus.PartiallyFilled.ToString()
                    : OrderLifecycleStatus.Filled.ToString();
            dto.RequestedQuantity = Math.Max(dto.RequestedQuantity, domainEvent.FilledAmount);
            dto.FilledQuantity = domainEvent.FilledAmount;
            dto.AveragePrice = domainEvent.AveragePrice;
            dto.Cost = domainEvent.Cost;
            dto.Fees = domainEvent.Fees;
            dto.CostCurrency = string.IsNullOrWhiteSpace(dto.CostCurrency) ? dto.QuoteCurrency : dto.CostCurrency;
            dto.FeesCurrency = string.IsNullOrWhiteSpace(dto.FeesCurrency) ? dto.QuoteCurrency : dto.FeesCurrency;
            dto.SubmittedAt = dto.SubmittedAt == default ? domainEvent.OccurredAt : dto.SubmittedAt;

            cache[domainEvent.OrderId] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(OrderCanceledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return ProjectTerminalStatusAsync(
            domainEvent.OrderId,
            domainEvent.Pair,
            domainEvent.Side,
            OrderLifecycleStatus.Canceled,
            domainEvent.OccurredAt,
            cancellationToken);
    }

    public Task ProjectAsync(OrderRejectedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return ProjectTerminalStatusAsync(
            domainEvent.OrderId,
            domainEvent.Pair,
            domainEvent.Side,
            OrderLifecycleStatus.Rejected,
            domainEvent.OccurredAt,
            cancellationToken);
    }

    public Task ProjectAsync(OrderLinkedToPositionEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            if (cache.TryGetValue(domainEvent.OrderId, out var dto))
            {
                dto.RelatedPositionId = domainEvent.PositionId;
            }
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

    private Task ProjectTerminalStatusAsync(
        string orderId,
        string pair,
        DomainOrderSide side,
        OrderLifecycleStatus status,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        return MutateAsync(cache =>
        {
            var quoteCurrency = ResolveQuoteCurrency(pair, null);
            var dto = cache.GetValueOrDefault(orderId) ?? new OrderReadModelDto
            {
                Id = orderId,
                Pair = pair.ToUpperInvariant(),
                QuoteCurrency = quoteCurrency,
                Side = side.ToString(),
                Type = DomainOrderType.Market.ToString(),
                CostCurrency = quoteCurrency,
                FeesCurrency = quoteCurrency,
                SubmittedAt = occurredAt
            };

            dto.Pair = string.IsNullOrWhiteSpace(dto.Pair) ? pair.ToUpperInvariant() : dto.Pair;
            dto.QuoteCurrency = string.IsNullOrWhiteSpace(dto.QuoteCurrency) ? quoteCurrency : dto.QuoteCurrency;
            dto.Side = side.ToString();
            dto.Status = status.ToString();
            dto.CostCurrency = string.IsNullOrWhiteSpace(dto.CostCurrency) ? dto.QuoteCurrency : dto.CostCurrency;
            dto.FeesCurrency = string.IsNullOrWhiteSpace(dto.FeesCurrency) ? dto.QuoteCurrency : dto.FeesCurrency;
            dto.SubmittedAt = dto.SubmittedAt == default ? occurredAt : dto.SubmittedAt;

            cache[orderId] = dto;
        }, cancellationToken);
    }

    private async Task MutateAsync(
        Action<ConcurrentDictionary<string, OrderReadModelDto>> mutate,
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
            _cache = new ConcurrentDictionary<string, OrderReadModelDto>(StringComparer.OrdinalIgnoreCase);
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new ConcurrentDictionary<string, OrderReadModelDto>(StringComparer.OrdinalIgnoreCase);
            _cacheLoaded = true;
            return;
        }

        var dtos = JsonSerializer.Deserialize<List<OrderReadModelDto>>(json, _jsonOptions)
                   ?? new List<OrderReadModelDto>();
        _cache = new ConcurrentDictionary<string, OrderReadModelDto>(
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
            .OrderByDescending(dto => dto.SubmittedAt)
            .ToList();
        var json = JsonSerializer.Serialize(dtos, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static OrderView MapOrderView(OrderReadModelDto dto)
    {
        var status = ParseStatus(dto.Status);
        var pair = MapPair(dto);

        return new OrderView
        {
            Id = OrderId.From(dto.Id),
            Pair = pair,
            Side = ParseSide(dto.Side),
            Type = ParseType(dto.Type),
            Status = status,
            RequestedQuantity = Quantity.Create(dto.RequestedQuantity),
            FilledQuantity = Quantity.Create(dto.FilledQuantity),
            SubmittedPrice = Price.Create(dto.SubmittedPrice),
            AveragePrice = Price.Create(dto.AveragePrice),
            Cost = Money.Create(dto.Cost, ResolveCurrency(dto.CostCurrency, pair.QuoteCurrency)),
            Fees = Money.Create(dto.Fees, ResolveCurrency(dto.FeesCurrency, pair.QuoteCurrency)),
            SignalRule = dto.SignalRule,
            SubmittedAt = dto.SubmittedAt,
            CanAffectPosition = CanAffectPosition(status, dto.FilledQuantity),
            IsTerminal = IsTerminal(status)
        };
    }

    private static TradeHistoryEntry MapTradeHistoryEntry(OrderReadModelDto dto)
    {
        var pair = MapPair(dto);

        return new TradeHistoryEntry
        {
            PositionId = PositionId.From(dto.RelatedPositionId!),
            Pair = pair,
            Type = ResolveTradeType(dto),
            Price = Price.Create(dto.AveragePrice),
            Quantity = Quantity.Create(dto.FilledQuantity),
            Cost = Money.Create(dto.Cost, ResolveCurrency(dto.CostCurrency, pair.QuoteCurrency)),
            Fees = Money.Create(dto.Fees, ResolveCurrency(dto.FeesCurrency, pair.QuoteCurrency)),
            Timestamp = dto.SubmittedAt,
            OrderId = dto.Id,
            Note = dto.Intent
        };
    }

    private static TradingPair MapPair(OrderReadModelDto dto)
    {
        return TradingPair.Create(dto.Pair, ResolveQuoteCurrency(dto.Pair, dto.QuoteCurrency));
    }

    private static TradeType ResolveTradeType(OrderReadModelDto dto)
    {
        if (ParseSide(dto.Side) == DomainOrderSide.Sell)
        {
            return TradeType.Sell;
        }

        return ParseIntent(dto.Intent) == OrderIntent.ExecuteDca ? TradeType.DCA : TradeType.Buy;
    }

    private static bool CanAffectPosition(OrderLifecycleStatus status, decimal filledQuantity)
    {
        return status is OrderLifecycleStatus.PartiallyFilled or OrderLifecycleStatus.Filled ||
               status == OrderLifecycleStatus.Canceled && filledQuantity > 0m;
    }

    private static bool IsTerminal(OrderLifecycleStatus status)
    {
        return status is OrderLifecycleStatus.Filled or OrderLifecycleStatus.Canceled or OrderLifecycleStatus.Rejected;
    }

    private static DomainOrderSide ParseSide(string? side)
    {
        return Enum.TryParse<DomainOrderSide>(side, ignoreCase: true, out var parsed)
            ? parsed
            : DomainOrderSide.Buy;
    }

    private static DomainOrderType ParseType(string? type)
    {
        return Enum.TryParse<DomainOrderType>(type, ignoreCase: true, out var parsed)
            ? parsed
            : DomainOrderType.Limit;
    }

    private static OrderLifecycleStatus ParseStatus(string? status)
    {
        return Enum.TryParse<OrderLifecycleStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : OrderLifecycleStatus.Submitted;
    }

    private static OrderIntent ParseIntent(string? intent)
    {
        return Enum.TryParse<OrderIntent>(intent, ignoreCase: true, out var parsed)
            ? parsed
            : OrderIntent.Unknown;
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

    private sealed class OrderReadModelDto
    {
        public string Id { get; set; } = string.Empty;
        public string Pair { get; set; } = string.Empty;
        public string QuoteCurrency { get; set; } = string.Empty;
        public string Side { get; set; } = DomainOrderSide.Buy.ToString();
        public string Type { get; set; } = DomainOrderType.Limit.ToString();
        public string Status { get; set; } = OrderLifecycleStatus.Submitted.ToString();
        public decimal RequestedQuantity { get; set; }
        public decimal FilledQuantity { get; set; }
        public decimal SubmittedPrice { get; set; }
        public decimal AveragePrice { get; set; }
        public decimal Cost { get; set; }
        public string CostCurrency { get; set; } = string.Empty;
        public decimal Fees { get; set; }
        public string FeesCurrency { get; set; } = string.Empty;
        public string? SignalRule { get; set; }
        public string Intent { get; set; } = OrderIntent.Unknown.ToString();
        public string? RelatedPositionId { get; set; }
        public DateTimeOffset SubmittedAt { get; set; }
    }
}
