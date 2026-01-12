using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// JSON file-based repository for Position aggregates.
/// Thread-safe implementation using file locking and in-memory cache.
/// </summary>
public sealed class JsonPositionRepository : IPositionRepository, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private ConcurrentDictionary<Guid, PositionDto> _cache = new();
    private bool _cacheLoaded;
    private bool _disposed;

    /// <summary>
    /// Creates a new JsonPositionRepository.
    /// </summary>
    /// <param name="filePath">The path to the JSON file for position storage.</param>
    public JsonPositionRepository(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        _filePath = filePath;
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
        await EnsureCacheLoadedAsync(cancellationToken);

        return _cache.TryGetValue(id.Value, out var dto) ? PositionMapper.FromDto(dto) : null;
    }

    /// <inheritdoc />
    public async Task<Position?> GetByPairAsync(TradingPair pair, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        await EnsureCacheLoadedAsync(cancellationToken);

        var dto = _cache.Values
            .FirstOrDefault(p => !p.IsClosed &&
                                 p.Pair.Symbol.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase));

        return dto != null ? PositionMapper.FromDto(dto) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Position>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken);

        return _cache.Values
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
        await EnsureCacheLoadedAsync(cancellationToken);

        return _cache.Values
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
        await EnsureCacheLoadedAsync(cancellationToken);

        return _cache.Values
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
        await EnsureCacheLoadedAsync(cancellationToken);

        // Preserve existing portfolio association if present
        PortfolioId? portfolioId = null;
        if (_cache.TryGetValue(position.Id.Value, out var existingDto))
        {
            portfolioId = PositionMapper.GetPortfolioId(existingDto);
        }

        var dto = PositionMapper.ToDto(position, portfolioId);
        _cache[position.Id.Value] = dto;

        await PersistCacheAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveManyAsync(IEnumerable<Position> positions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(positions);
        await EnsureCacheLoadedAsync(cancellationToken);

        foreach (var position in positions)
        {
            PortfolioId? portfolioId = null;
            if (_cache.TryGetValue(position.Id.Value, out var existingDto))
            {
                portfolioId = PositionMapper.GetPortfolioId(existingDto);
            }

            var dto = PositionMapper.ToDto(position, portfolioId);
            _cache[position.Id.Value] = dto;
        }

        await PersistCacheAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(PositionId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken);

        if (_cache.TryRemove(id.Value, out _))
        {
            await PersistCacheAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsForPairAsync(TradingPair pair, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        await EnsureCacheLoadedAsync(cancellationToken);

        return _cache.Values.Any(p => !p.IsClosed &&
                                      p.Pair.Symbol.Equals(pair.Symbol, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken);
        return _cache.Values.Count(p => !p.IsClosed);
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
        await EnsureCacheLoadedAsync(cancellationToken);

        if (_cache.TryGetValue(positionId.Value, out var dto))
        {
            dto.PortfolioId = portfolioId.Value;
            await PersistCacheAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Reloads the cache from the file.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            _cacheLoaded = false;
            await LoadCacheAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cacheLoaded) return;

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!_cacheLoaded)
            {
                await LoadCacheAsync(cancellationToken);
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

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
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

    private async Task PersistCacheAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dtos = _cache.Values.ToList();
            var json = JsonSerializer.Serialize(dtos, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _fileLock.Dispose();
        _disposed = true;
    }
}
