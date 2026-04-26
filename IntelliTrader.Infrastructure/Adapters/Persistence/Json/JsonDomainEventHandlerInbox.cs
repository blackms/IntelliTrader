using System.Collections.Concurrent;
using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// JSON-backed inbox that records which domain event handlers processed each event.
/// </summary>
public sealed class JsonDomainEventHandlerInbox : IDomainEventHandlerInbox, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private ConcurrentDictionary<string, DomainEventHandlerInboxEntryDto> _cache = new();
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonDomainEventHandlerInbox(string filePath)
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

    public async Task<bool> HasProcessedAsync(
        Guid eventId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        Validate(eventId, handlerName);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache.ContainsKey(CreateKey(eventId, handlerName));
    }

    public async Task MarkProcessedAsync(
        Guid eventId,
        string handlerName,
        CancellationToken cancellationToken = default)
    {
        Validate(eventId, handlerName);
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);

        var key = CreateKey(eventId, handlerName);
        if (!_cache.TryAdd(
                key,
                new DomainEventHandlerInboxEntryDto
                {
                    EventId = eventId,
                    HandlerName = handlerName,
                    ProcessedAt = DateTimeOffset.UtcNow
                }))
        {
            return;
        }

        await PersistCacheAsync(cancellationToken).ConfigureAwait(false);
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
            if (_cacheLoaded)
            {
                return;
            }

            if (!File.Exists(_filePath))
            {
                _cache = new ConcurrentDictionary<string, DomainEventHandlerInboxEntryDto>();
                _cacheLoaded = true;
                return;
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                _cache = new ConcurrentDictionary<string, DomainEventHandlerInboxEntryDto>();
                _cacheLoaded = true;
                return;
            }

            var entries = JsonSerializer.Deserialize<List<DomainEventHandlerInboxEntryDto>>(json, _jsonOptions)
                          ?? new List<DomainEventHandlerInboxEntryDto>();

            _cache = new ConcurrentDictionary<string, DomainEventHandlerInboxEntryDto>(
                entries.ToDictionary(entry => CreateKey(entry.EventId, entry.HandlerName)));
            _cacheLoaded = true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task PersistCacheAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entries = _cache.Values
                .OrderBy(entry => entry.ProcessedAt)
                .ToList();
            var json = JsonSerializer.Serialize(entries, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static void Validate(Guid eventId, string handlerName)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event ID cannot be empty.", nameof(eventId));

        if (string.IsNullOrWhiteSpace(handlerName))
            throw new ArgumentException("Handler name cannot be null or empty.", nameof(handlerName));
    }

    private static string CreateKey(Guid eventId, string handlerName)
    {
        return $"{eventId:N}:{handlerName}";
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _fileLock.Dispose();
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private sealed class DomainEventHandlerInboxEntryDto
    {
        public Guid EventId { get; set; }
        public string HandlerName { get; set; } = string.Empty;
        public DateTimeOffset ProcessedAt { get; set; }
    }
}
