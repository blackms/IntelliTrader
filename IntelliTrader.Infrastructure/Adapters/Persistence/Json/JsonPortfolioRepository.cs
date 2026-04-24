using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Transactions;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// JSON file-based repository for Portfolio aggregates.
/// Supports a single default portfolio while allowing additional named portfolios.
/// </summary>
public sealed class JsonPortfolioRepository : IPortfolioRepository, IJsonTransactionalResource, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonTransactionCoordinator _transactionCoordinator;
    private ConcurrentDictionary<Guid, PortfolioDto> _cache = new();
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonPortfolioRepository(string filePath, JsonTransactionCoordinator? transactionCoordinator = null)
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

    public async Task<Portfolio?> GetByIdAsync(PortfolioId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetReadCache();
        return cache.TryGetValue(id.Value, out var dto) ? PortfolioMapper.FromDto(dto) : null;
    }

    public async Task<Portfolio?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var dto = GetReadCache().Values.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        return dto is null ? null : PortfolioMapper.FromDto(dto);
    }

    public async Task<Portfolio?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var dto = GetReadCache().Values
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.CreatedAt)
            .FirstOrDefault();

        return dto is null ? null : PortfolioMapper.FromDto(dto);
    }

    public async Task<IReadOnlyList<Portfolio>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(PortfolioMapper.FromDto)
            .ToList();
    }

    public async Task SaveAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();
        var isDefault = ResolveDefaultFlag(cache, portfolio.Id.Value);
        var dto = PortfolioMapper.ToDto(portfolio, isDefault);
        cache[portfolio.Id.Value] = dto;

        if (isDefault)
        {
            PromoteDefaultPortfolio(cache, portfolio.Id.Value);
        }

        if (!_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(PortfolioId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();
        var removedWasDefault = cache.TryGetValue(id.Value, out var existing) && existing.IsDefault;
        if (!cache.TryRemove(id.Value, out _))
        {
            return;
        }

        if (removedWasDefault)
        {
            PromoteOldestPortfolioAsDefault(cache);
        }

        if (!_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return GetReadCache().Values.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

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

    private bool ResolveDefaultFlag(
        ConcurrentDictionary<Guid, PortfolioDto> cache,
        Guid portfolioId)
    {
        if (cache.TryGetValue(portfolioId, out var existing))
        {
            return existing.IsDefault || !cache.Values.Any(p => p.IsDefault);
        }

        return cache.IsEmpty || !cache.Values.Any(p => p.IsDefault);
    }

    private static void PromoteDefaultPortfolio(
        ConcurrentDictionary<Guid, PortfolioDto> cache,
        Guid defaultPortfolioId)
    {
        foreach (var dto in cache.Values)
        {
            dto.IsDefault = dto.Id == defaultPortfolioId;
        }
    }

    private static void PromoteOldestPortfolioAsDefault(ConcurrentDictionary<Guid, PortfolioDto> cache)
    {
        var nextDefault = cache.Values.OrderBy(p => p.CreatedAt).FirstOrDefault();
        if (nextDefault is null)
            return;

        PromoteDefaultPortfolio(cache, nextDefault.Id);
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
            _cache = new ConcurrentDictionary<Guid, PortfolioDto>();
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new ConcurrentDictionary<Guid, PortfolioDto>();
            _cacheLoaded = true;
            return;
        }

        var dtos = JsonSerializer.Deserialize<List<PortfolioDto>>(json, _jsonOptions) ?? new List<PortfolioDto>();
        _cache = new ConcurrentDictionary<Guid, PortfolioDto>(dtos.ToDictionary(d => d.Id));

        if (_cache.Count > 0 && !_cache.Values.Any(p => p.IsDefault))
        {
            PromoteOldestPortfolioAsDefault(_cache);
        }

        _cacheLoaded = true;
    }

    private ConcurrentDictionary<Guid, PortfolioDto> GetReadCache()
    {
        if (_transactionCoordinator.TryGetState(this, out ConcurrentDictionary<Guid, PortfolioDto>? stagedCache))
        {
            return stagedCache;
        }

        return _cache;
    }

    private ConcurrentDictionary<Guid, PortfolioDto> GetWriteCache()
    {
        if (!_transactionCoordinator.HasActiveTransaction)
        {
            return _cache;
        }

        return _transactionCoordinator.GetOrCreateState(this, CloneCache);
    }

    private ConcurrentDictionary<Guid, PortfolioDto> CloneCache()
    {
        var json = JsonSerializer.Serialize(_cache.Values.ToList(), _jsonOptions);
        var dtos = JsonSerializer.Deserialize<List<PortfolioDto>>(json, _jsonOptions) ?? new List<PortfolioDto>();
        var clone = new ConcurrentDictionary<Guid, PortfolioDto>(dtos.ToDictionary(d => d.Id));

        if (clone.Count > 0 && !clone.Values.Any(p => p.IsDefault))
        {
            PromoteOldestPortfolioAsDefault(clone);
        }

        return clone;
    }

    private async Task PersistCacheAsync(
        ConcurrentDictionary<Guid, PortfolioDto> cache,
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

            var dtos = cache.Values
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.CreatedAt)
                .ToList();

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
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, PortfolioDto>? stagedCache))
        {
            return Task.FromResult<JsonPreparedWrite?>(null);
        }

        var dtos = stagedCache.Values
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.CreatedAt)
            .ToList();

        var json = JsonSerializer.Serialize(dtos, _jsonOptions);
        return Task.FromResult<JsonPreparedWrite?>(new JsonPreparedWrite(_filePath, json));
    }

    async Task IJsonTransactionalResource.AcceptCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, PortfolioDto>? stagedCache))
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
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, PortfolioDto>? _))
        {
            return Task.CompletedTask;
        }

        return ReloadAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _fileLock.Dispose();
        _disposed = true;
    }
}
