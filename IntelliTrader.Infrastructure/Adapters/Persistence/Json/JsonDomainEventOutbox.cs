using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Infrastructure.Transactions;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// JSON-backed transactional outbox for domain events.
/// </summary>
public sealed class JsonDomainEventOutbox : IDomainEventOutbox, IJsonTransactionalResource, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonTransactionCoordinator _transactionCoordinator;
    private ConcurrentDictionary<Guid, DomainEventOutboxMessageDto> _cache = new();
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonDomainEventOutbox(string filePath, JsonTransactionCoordinator? transactionCoordinator = null)
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

    public async Task EnqueueAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        var eventList = domainEvents.ToList();
        if (eventList.Count == 0)
        {
            return;
        }

        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        var cache = GetWriteCache();

        foreach (var domainEvent in eventList)
        {
            cache.TryAdd(domainEvent.EventId, ToDto(domainEvent));
        }

        if (!_transactionCoordinator.HasActiveTransaction)
        {
            await PersistCacheAsync(cache, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<DomainEventOutboxMessage>> GetUnprocessedAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");

        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .Where(message => message.ProcessedAt is null)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.EnqueuedAt)
            .Take(batchSize)
            .Select(ToMessage)
            .ToList();
    }

    public async Task<IReadOnlyList<DomainEventOutboxMessage>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        return GetReadCache().Values
            .OrderBy(message => message.EnqueuedAt)
            .Select(ToMessage)
            .ToList();
    }

    public async Task MarkProcessedAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event ID cannot be empty.", nameof(eventId));

        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var readCache = GetReadCache();
        if (!readCache.TryGetValue(eventId, out var existing) || existing.ProcessedAt is not null)
        {
            return;
        }

        var cache = GetWriteCache();
        if (cache.TryGetValue(eventId, out var message) && message.ProcessedAt is null)
        {
            message.ProcessedAt = DateTimeOffset.UtcNow;
        }

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
        {
            return;
        }

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
            _cache = new ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>();
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>();
            _cacheLoaded = true;
            return;
        }

        var dtos = JsonSerializer.Deserialize<List<DomainEventOutboxMessageDto>>(json, _jsonOptions)
                   ?? new List<DomainEventOutboxMessageDto>();

        _cache = new ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>(
            dtos.ToDictionary(dto => dto.EventId));
        _cacheLoaded = true;
    }

    private ConcurrentDictionary<Guid, DomainEventOutboxMessageDto> GetReadCache()
    {
        if (_transactionCoordinator.TryGetState(this, out ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>? stagedCache))
        {
            return stagedCache!;
        }

        return _cache;
    }

    private ConcurrentDictionary<Guid, DomainEventOutboxMessageDto> GetWriteCache()
    {
        if (!_transactionCoordinator.HasActiveTransaction)
        {
            return _cache;
        }

        return _transactionCoordinator.GetOrCreateState(this, CloneCache);
    }

    private ConcurrentDictionary<Guid, DomainEventOutboxMessageDto> CloneCache()
    {
        var json = JsonSerializer.Serialize(_cache.Values.ToList(), _jsonOptions);
        var dtos = JsonSerializer.Deserialize<List<DomainEventOutboxMessageDto>>(json, _jsonOptions)
                   ?? new List<DomainEventOutboxMessageDto>();

        return new ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>(
            dtos.ToDictionary(dto => dto.EventId));
    }

    private async Task PersistCacheAsync(
        ConcurrentDictionary<Guid, DomainEventOutboxMessageDto> cache,
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
                .OrderBy(message => message.EnqueuedAt)
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
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>? stagedCache))
        {
            return Task.FromResult<JsonPreparedWrite?>(null);
        }

        var dtos = stagedCache!.Values
            .OrderBy(message => message.EnqueuedAt)
            .ToList();

        var json = JsonSerializer.Serialize(dtos, _jsonOptions);
        return Task.FromResult<JsonPreparedWrite?>(new JsonPreparedWrite(_filePath, json));
    }

    async Task IJsonTransactionalResource.AcceptCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>? stagedCache))
        {
            return;
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cache = stagedCache!;
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
        if (!transaction.TryGetState(this, out ConcurrentDictionary<Guid, DomainEventOutboxMessageDto>? _))
        {
            return Task.CompletedTask;
        }

        return ReloadAsync(cancellationToken);
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

    private DomainEventOutboxMessageDto ToDto(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        return new DomainEventOutboxMessageDto
        {
            EventId = domainEvent.EventId,
            EventType = eventType.AssemblyQualifiedName ?? eventType.FullName ?? eventType.Name,
            Payload = JsonSerializer.Serialize(domainEvent, eventType, _jsonOptions),
            OccurredAt = domainEvent.OccurredAt,
            EnqueuedAt = DateTimeOffset.UtcNow
        };
    }

    private static DomainEventOutboxMessage ToMessage(DomainEventOutboxMessageDto dto)
    {
        return new DomainEventOutboxMessage(
            dto.EventId,
            dto.EventType,
            dto.Payload,
            dto.OccurredAt,
            dto.EnqueuedAt,
            dto.ProcessedAt);
    }

    private sealed class DomainEventOutboxMessageDto
    {
        public Guid EventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTimeOffset OccurredAt { get; set; }
        public DateTimeOffset EnqueuedAt { get; set; }
        public DateTimeOffset? ProcessedAt { get; set; }
    }
}
