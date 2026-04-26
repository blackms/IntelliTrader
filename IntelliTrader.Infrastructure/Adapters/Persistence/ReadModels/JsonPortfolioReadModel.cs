using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

/// <summary>
/// Durable read-side projection for portfolio queries.
/// </summary>
public sealed class JsonPortfolioReadModel : IPortfolioReadModel, IPortfolioReadModelProjectionWriter, IDisposable
{
    private readonly string _filePath;
    private readonly string? _legacyPortfolioFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private ConcurrentDictionary<string, PortfolioReadModelDto> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonPortfolioReadModel(string filePath, string? legacyPortfolioFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        _filePath = filePath;
        _legacyPortfolioFilePath = legacyPortfolioFilePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<PortfolioReadModelEntry?> GetByIdAsync(
        PortfolioId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.TryGetValue(id.ToString(), out var dto) ? MapEntry(dto) : null;
    }

    public async Task<PortfolioReadModelEntry?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty.", nameof(name));

        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.Values
            .FirstOrDefault(dto => dto.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } dto
            ? MapEntry(dto)
            : null;
    }

    public async Task<PortfolioReadModelEntry?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return _cache.Values
            .OrderByDescending(dto => dto.IsDefault)
            .ThenBy(dto => dto.CreatedAt)
            .Select(MapEntry)
            .FirstOrDefault();
    }

    public Task ProjectAsync(PositionAddedToPortfolio domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var dto = GetOrCreate(cache, domainEvent.PortfolioId, domainEvent.Cost.Currency, domainEvent.OccurredAt);
            dto.ActivePositions[domainEvent.PositionId.ToString()] = new ActivePositionReadModelDto
            {
                PositionId = domainEvent.PositionId.ToString(),
                Pair = domainEvent.Pair.Symbol,
                QuoteCurrency = domainEvent.Pair.QuoteCurrency
            };
            dto.PositionCosts[domainEvent.PositionId.ToString()] = domainEvent.Cost.Amount;
            dto.ActivePositionCount = domainEvent.ActivePositionCount;
            dto.ReservedBalance = Math.Max(dto.ReservedBalance, SumPositionCosts(dto));
            dto.AvailableBalance = Math.Max(0m, dto.TotalBalance - dto.ReservedBalance);
            dto.LastUpdatedAt = domainEvent.OccurredAt;

            cache[dto.Id] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(PositionRemovedFromPortfolio domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var dto = GetOrCreate(cache, domainEvent.PortfolioId, domainEvent.Proceeds.Currency, domainEvent.OccurredAt);
            dto.ActivePositions.Remove(domainEvent.PositionId.ToString());
            dto.PositionCosts.Remove(domainEvent.PositionId.ToString());
            dto.ActivePositionCount = domainEvent.ActivePositionCount;
            dto.ReservedBalance = SumPositionCosts(dto);
            dto.AvailableBalance = Math.Max(0m, dto.TotalBalance - dto.ReservedBalance);
            dto.LastUpdatedAt = domainEvent.OccurredAt;

            cache[dto.Id] = dto;
        }, cancellationToken);
    }

    public Task ProjectAsync(PortfolioBalanceChanged domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(cache =>
        {
            var dto = GetOrCreate(cache, domainEvent.PortfolioId, domainEvent.NewTotal.Currency, domainEvent.OccurredAt);
            dto.Market = domainEvent.NewTotal.Currency;
            dto.TotalBalance = domainEvent.NewTotal.Amount;
            dto.AvailableBalance = domainEvent.NewAvailable.Amount;
            dto.ReservedBalance = Math.Max(0m, domainEvent.NewTotal.Amount - domainEvent.NewAvailable.Amount);
            dto.LastUpdatedAt = domainEvent.OccurredAt;

            cache[dto.Id] = dto;
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
        Action<ConcurrentDictionary<string, PortfolioReadModelDto>> mutate,
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
        if (await TryLoadProjectionAsync(cancellationToken).ConfigureAwait(false))
        {
            _cacheLoaded = true;
            return;
        }

        if (await TryBootstrapFromLegacyPortfoliosAsync(cancellationToken).ConfigureAwait(false))
        {
            await PersistCacheUnlockedAsync(cancellationToken).ConfigureAwait(false);
            _cacheLoaded = true;
            return;
        }

        _cache = new ConcurrentDictionary<string, PortfolioReadModelDto>(StringComparer.OrdinalIgnoreCase);
        _cacheLoaded = true;
    }

    private async Task<bool> TryLoadProjectionAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return false;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var dtos = JsonSerializer.Deserialize<List<PortfolioReadModelDto>>(json, _jsonOptions)
                   ?? new List<PortfolioReadModelDto>();
        if (dtos.Count == 0)
        {
            return false;
        }

        _cache = new ConcurrentDictionary<string, PortfolioReadModelDto>(
            dtos.ToDictionary(dto => dto.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private async Task<bool> TryBootstrapFromLegacyPortfoliosAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_legacyPortfolioFilePath) || !File.Exists(_legacyPortfolioFilePath))
        {
            return false;
        }

        var json = await File.ReadAllTextAsync(_legacyPortfolioFilePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var portfolios = JsonSerializer.Deserialize<List<PortfolioDto>>(json, _jsonOptions)
                         ?? new List<PortfolioDto>();
        if (portfolios.Count == 0)
        {
            return false;
        }

        if (!portfolios.Any(portfolio => portfolio.IsDefault))
        {
            portfolios.OrderBy(portfolio => portfolio.CreatedAt).First().IsDefault = true;
        }

        _cache = new ConcurrentDictionary<string, PortfolioReadModelDto>(
            portfolios.Select(MapLegacyPortfolioDto)
                .ToDictionary(dto => dto.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private async Task PersistCacheUnlockedAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dtos = _cache.Values
            .OrderByDescending(dto => dto.IsDefault)
            .ThenBy(dto => dto.CreatedAt)
            .ToList();
        var json = JsonSerializer.Serialize(dtos, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static PortfolioReadModelDto GetOrCreate(
        ConcurrentDictionary<string, PortfolioReadModelDto> cache,
        PortfolioId id,
        string market,
        DateTimeOffset occurredAt)
    {
        return cache.GetValueOrDefault(id.ToString()) ?? new PortfolioReadModelDto
        {
            Id = id.ToString(),
            Name = cache.IsEmpty ? "Default" : id.ToString(),
            Market = market,
            MaxPositions = 1,
            CreatedAt = occurredAt,
            IsDefault = cache.IsEmpty
        };
    }

    private static PortfolioReadModelEntry MapEntry(PortfolioReadModelDto dto)
    {
        var market = ResolveMarket(dto.Market);

        return new PortfolioReadModelEntry
        {
            Id = PortfolioId.From(dto.Id),
            Name = dto.Name,
            Market = market,
            TotalBalance = Money.Create(dto.TotalBalance, market),
            AvailableBalance = Money.Create(dto.AvailableBalance, market),
            ReservedBalance = Money.Create(dto.ReservedBalance, market),
            ActivePositionCount = dto.ActivePositionCount,
            MaxPositions = dto.MaxPositions <= 0 ? 1 : dto.MaxPositions,
            MinPositionCost = Money.Create(dto.MinPositionCost, market),
            InvestedBalance = Money.Create(dto.ReservedBalance, market),
            CreatedAt = dto.CreatedAt,
            LastUpdatedAt = dto.LastUpdatedAt,
            IsDefault = dto.IsDefault
        };
    }

    private static PortfolioReadModelDto MapLegacyPortfolioDto(PortfolioDto dto)
    {
        return new PortfolioReadModelDto
        {
            Id = dto.Id.ToString(),
            Name = dto.Name,
            Market = ResolveMarket(dto.Market),
            TotalBalance = dto.Balance.Total,
            AvailableBalance = dto.Balance.Available,
            ReservedBalance = dto.Balance.Reserved,
            ActivePositionCount = dto.ActivePositions.Count,
            MaxPositions = dto.MaxPositions,
            MinPositionCost = dto.MinPositionCost,
            CreatedAt = dto.CreatedAt,
            IsDefault = dto.IsDefault,
            ActivePositions = dto.ActivePositions.ToDictionary(
                active => active.PositionId.ToString(),
                active => new ActivePositionReadModelDto
                {
                    PositionId = active.PositionId.ToString(),
                    Pair = active.Pair.Symbol,
                    QuoteCurrency = active.Pair.QuoteCurrency
                },
                StringComparer.OrdinalIgnoreCase),
            PositionCosts = dto.PositionCosts.ToDictionary(
                cost => cost.PositionId.ToString(),
                cost => cost.Amount,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static decimal SumPositionCosts(PortfolioReadModelDto dto)
    {
        return dto.PositionCosts.Values.Sum();
    }

    private static string ResolveMarket(string? market)
    {
        return string.IsNullOrWhiteSpace(market) ? "USDT" : market.ToUpperInvariant();
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

    private sealed class PortfolioReadModelDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Market { get; set; } = "USDT";
        public decimal TotalBalance { get; set; }
        public decimal AvailableBalance { get; set; }
        public decimal ReservedBalance { get; set; }
        public int ActivePositionCount { get; set; }
        public int MaxPositions { get; set; }
        public decimal MinPositionCost { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastUpdatedAt { get; set; }
        public bool IsDefault { get; set; }
        public Dictionary<string, ActivePositionReadModelDto> ActivePositions { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> PositionCosts { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ActivePositionReadModelDto
    {
        public string PositionId { get; set; } = string.Empty;
        public string Pair { get; set; } = string.Empty;
        public string QuoteCurrency { get; set; } = string.Empty;
    }
}
