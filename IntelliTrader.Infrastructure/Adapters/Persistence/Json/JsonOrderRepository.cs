using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Transactions;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// JSON file-based repository for persisted order lifecycles.
/// </summary>
public sealed class JsonOrderRepository : IOrderRepository, IJsonTransactionalResource, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonTransactionCoordinator _transactionCoordinator;
    private ConcurrentDictionary<string, OrderLifecycleDto> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonOrderRepository(string filePath, JsonTransactionCoordinator? transactionCoordinator = null)
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

    public async Task<OrderLifecycle?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetReadCache();
        return cache.TryGetValue(id.Value, out var dto) ? OrderLifecycleMapper.FromDto(dto) : null;
    }

    public async Task<IReadOnlyList<OrderLifecycle>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .OrderByDescending(dto => dto.SubmittedAt)
            .Select(OrderLifecycleMapper.FromDto)
            .ToList();
    }

    public async Task SaveAsync(OrderLifecycle order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var cache = GetWriteCache();
        cache[order.Id.Value] = OrderLifecycleMapper.ToDto(order);

        if (!_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
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

    private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cacheLoaded)
            return;

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
            _cache = new ConcurrentDictionary<string, OrderLifecycleDto>(StringComparer.OrdinalIgnoreCase);
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new ConcurrentDictionary<string, OrderLifecycleDto>(StringComparer.OrdinalIgnoreCase);
            _cacheLoaded = true;
            return;
        }

        var dtos = JsonSerializer.Deserialize<List<OrderLifecycleDto>>(json, _jsonOptions)
                   ?? new List<OrderLifecycleDto>();

        _cache = new ConcurrentDictionary<string, OrderLifecycleDto>(
            dtos.ToDictionary(dto => dto.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        _cacheLoaded = true;
    }

    private ConcurrentDictionary<string, OrderLifecycleDto> GetReadCache()
    {
        if (_transactionCoordinator.TryGetState(this, out ConcurrentDictionary<string, OrderLifecycleDto>? stagedCache))
        {
            return stagedCache;
        }

        return _cache;
    }

    private ConcurrentDictionary<string, OrderLifecycleDto> GetWriteCache()
    {
        if (!_transactionCoordinator.HasActiveTransaction)
        {
            return _cache;
        }

        return _transactionCoordinator.GetOrCreateState(this, CloneCache);
    }

    private ConcurrentDictionary<string, OrderLifecycleDto> CloneCache()
    {
        var json = JsonSerializer.Serialize(_cache.Values.ToList(), _jsonOptions);
        var dtos = JsonSerializer.Deserialize<List<OrderLifecycleDto>>(json, _jsonOptions)
                   ?? new List<OrderLifecycleDto>();

        return new ConcurrentDictionary<string, OrderLifecycleDto>(
            dtos.ToDictionary(dto => dto.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task PersistCacheAsync(
        ConcurrentDictionary<string, OrderLifecycleDto> cache,
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
                .OrderByDescending(dto => dto.SubmittedAt)
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
        if (!transaction.TryGetState(this, out ConcurrentDictionary<string, OrderLifecycleDto>? stagedCache))
        {
            return Task.FromResult<JsonPreparedWrite?>(null);
        }

        var dtos = stagedCache.Values
            .OrderByDescending(dto => dto.SubmittedAt)
            .ToList();

        var json = JsonSerializer.Serialize(dtos, _jsonOptions);
        return Task.FromResult<JsonPreparedWrite?>(new JsonPreparedWrite(_filePath, json));
    }

    async Task IJsonTransactionalResource.AcceptCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.TryGetState(this, out ConcurrentDictionary<string, OrderLifecycleDto>? stagedCache))
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
        if (_disposed)
            return;

        _fileLock.Dispose();
        _disposed = true;
    }
}
