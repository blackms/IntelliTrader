using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Transactions;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// JSON file-based repository for Position aggregates.
/// Thread-safe implementation using file locking and in-memory cache.
/// </summary>
public sealed class JsonPositionRepository : IPositionRepository, IJsonTransactionalResource, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonTransactionCoordinator _transactionCoordinator;
    private ConcurrentDictionary<Guid, PositionDto> _cache = new();
    private bool _cacheLoaded;
    private bool _disposed;

    /// <summary>
    /// Creates a new JsonPositionRepository.
    /// </summary>
    /// <param name="filePath">The path to the JSON file for position storage.</param>
    public JsonPositionRepository(string filePath, JsonTransactionCoordinator? transactionCoordinator = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        _filePath = filePath;
        _transactionCoordinator = transactionCoordinator ?? JsonTransactionCoordinator.Shared;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public async Task<Position?> GetByIdAsync(PositionId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetReadCache();
        return cache.TryGetValue(id.Value, out var dto) ? PositionMapper.FromDto(dto) : null;
    }

    /// <inheritdoc />
    public async Task<Position?> GetByPairAsync(TradingPair pair, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var dto = GetReadCache().Values
            .FirstOrDefault(p => !p.IsClosed &&
                                 p.Pair.Symbol.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase));

        return dto != null ? PositionMapper.FromDto(dto) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Position>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .Where(p => !p.IsClosed)
            .Select(PositionMapper.FromDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Position>> GetByPortfolioAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolioId);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .Where(p => p.PortfolioId == portfolioId.Value)
            .Select(PositionMapper.FromDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Position>> GetClosedPositionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .Where(p => p.IsClosed &&
                        p.ClosedAt.HasValue &&
                        p.ClosedAt.Value >= from &&
                        p.ClosedAt.Value <= to)
            .Select(PositionMapper.FromDto)
            .ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(Position position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();

        // Preserve existing portfolio association if present
        PortfolioId? portfolioId = null;
        if (cache.TryGetValue(position.Id.Value, out var existingDto))
        {
            portfolioId = PositionMapper.GetPortfolioId(existingDto);
        }

        var dto = PositionMapper.ToDto(position, portfolioId);
        cache[position.Id.Value] = dto;

        if (!_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SaveManyAsync(IEnumerable<Position> positions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();
        foreach (var position in positions)
        {
            PortfolioId? portfolioId = null;
            if (cache.TryGetValue(position.Id.Value, out var existingDto))
            {
                portfolioId = PositionMapper.GetPortfolioId(existingDto);
            }

            var dto = PositionMapper.ToDto(position, portfolioId);
            cache[position.Id.Value] = dto;
        }

        if (!_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(PositionId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();
        if (cache.TryRemove(id.Value, out _) && !_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsForPairAsync(TradingPair pair, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values.Any(p => !p.IsClosed &&
                                              p.Pair.Symbol.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return GetReadCache().Values.Count(p => !p.IsClosed);
    }

    /// <summary>
    /// Associates a position with a portfolio.
    /// </summary>
    public async Task AssociateWithPortfolioAsync(
        PositionId positionId,
        PortfolioId portfolioId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positionId);
        ArgumentNullException.ThrowIfNull(portfolioId);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();
        if (cache.TryGetValue(positionId.Value, out var dto))
        {
            dto.PortfolioId = portfolioId.Value;

            if (!_transactionCoordinator.HasActiveTransaction)
            {
                await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reloads the cache from the file.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cacheLoaded = false;
            await LoadCacheAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cacheLoaded) return;

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_cacheLoaded)
            {
                await LoadCacheAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            _cache = new ConcurrentDictionary<Guid, PositionDto>();
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new ConcurrentDictionary<Guid, PositionDto>();
            _cacheLoaded = true;
            return;
        }

        var dtos = JsonSerializer.Deserialize<List<PositionDto>>(json, _jsonOptions) ?? new List<PositionDto>();
        _cache = new ConcurrentDictionary<Guid, PositionDto>(
            dtos.ToDictionary(d => d.Id));
        _cacheLoaded = true;
    }

    private ConcurrentDictionary<Guid, PositionDto> GetReadCache()
    {
        if (_transactionCoordinator.TryGetState(this, out ConcurrentDictionary<Guid, PositionDto>? stagedCache))
        {
            return stagedCache;
        }

        return _cache;
    }

    private ConcurrentDictionary<Guid, PositionDto> GetWriteCache()
    {
        if (!_transactionCoordinator.HasActiveTransaction)
        {
            return _cache;
        }

        return _transactionCoordinator.GetOrCreateState(this, CloneCache);
    }

    private ConcurrentDictionary<Guid, PositionDto> CloneCache()
    {
        var json = JsonSerializer.Serialize(_cache.Values.ToList(), _jsonOptions);
        var dtos = JsonSerializer.Deserialize<List<PositionDto>>(json, _jsonOptions) ?? new List<PositionDto>();

        return new ConcurrentDictionary<Guid, PositionDto>(dtos.ToDictionary(d => d.Id));
    }

    private async Task PersistCacheAsync(
        ConcurrentDictionary<Guid, PositionDto> cache,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dtos = cache.Values.ToList();
            var json = JsonSerializer.Serialize(dtos, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    Task<JsonPreparedWrite?> IJsonTransactionalResource.PrepareCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, PositionDto>? stagedCache))
        {
            return Task.FromResult<JsonPreparedWrite?>(null);
        }

        var json = JsonSerializer.Serialize(stagedCache.Values.ToList(), _jsonOptions);
        return Task.FromResult<JsonPreparedWrite?>(new JsonPreparedWrite(_filePath, json));
    }

    async Task IJsonTransactionalResource.AcceptCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, PositionDto>? stagedCache))
        {
            return;
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cache = stagedCache;
            _cacheLoaded = true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    Task IJsonTransactionalResource.RollbackAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _fileLock.Dispose();
        _disposed = true;
    }
}
