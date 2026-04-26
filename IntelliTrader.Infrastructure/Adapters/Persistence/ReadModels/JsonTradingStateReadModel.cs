using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

/// <summary>
/// Durable read-side projection for operational trading state.
/// </summary>
public sealed class JsonTradingStateReadModel :
    ITradingStateReadModel,
    ITradingStateReadModelProjectionWriter,
    IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private TradingStateReadModelDto _cache = new();
    private bool _cacheLoaded;
    private bool _disposed;

    public JsonTradingStateReadModel(string filePath)
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

    public async Task<TradingStateReadModelEntry> GetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return MapEntry(_cache);
    }

    public Task ProjectAsync(TradingSuspendedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(dto =>
        {
            dto.IsTradingSuspended = true;
            dto.SuspensionReason = domainEvent.Reason.ToString();
            dto.IsForcedSuspension = domainEvent.IsForced;
            dto.SuspendedAt = domainEvent.OccurredAt;
            dto.ResumedAt = null;
            dto.LastChangedAt = domainEvent.OccurredAt;
            dto.OpenPositionsAtSuspension = domainEvent.OpenPositions;
            dto.PendingOrdersAtSuspension = domainEvent.PendingOrders;
            dto.ChangedBy = domainEvent.SuspendedBy;
            dto.Details = domainEvent.Details;
        }, cancellationToken);
    }

    public Task ProjectAsync(TradingResumedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return MutateAsync(dto =>
        {
            dto.IsTradingSuspended = false;
            dto.SuspensionReason = null;
            dto.IsForcedSuspension = false;
            dto.SuspendedAt = null;
            dto.ResumedAt = domainEvent.OccurredAt;
            dto.LastChangedAt = domainEvent.OccurredAt;
            dto.OpenPositionsAtSuspension = 0;
            dto.PendingOrdersAtSuspension = 0;
            dto.ChangedBy = domainEvent.ResumedBy;
            dto.Details = null;
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
        Action<TradingStateReadModelDto> mutate,
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
            _cache = new TradingStateReadModelDto();
            _cacheLoaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            _cache = new TradingStateReadModelDto();
            _cacheLoaded = true;
            return;
        }

        _cache = JsonSerializer.Deserialize<TradingStateReadModelDto>(json, _jsonOptions)
                 ?? new TradingStateReadModelDto();
        _cacheLoaded = true;
    }

    private async Task PersistCacheUnlockedAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_cache, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static TradingStateReadModelEntry MapEntry(TradingStateReadModelDto dto)
    {
        return new TradingStateReadModelEntry
        {
            IsTradingSuspended = dto.IsTradingSuspended,
            SuspensionReason = ParseSuspensionReason(dto.SuspensionReason),
            IsForcedSuspension = dto.IsForcedSuspension,
            SuspendedAt = dto.SuspendedAt,
            ResumedAt = dto.ResumedAt,
            LastChangedAt = dto.LastChangedAt,
            OpenPositionsAtSuspension = dto.OpenPositionsAtSuspension,
            PendingOrdersAtSuspension = dto.PendingOrdersAtSuspension,
            ChangedBy = dto.ChangedBy,
            Details = dto.Details
        };
    }

    private static SuspensionReason? ParseSuspensionReason(string? reason)
    {
        return Enum.TryParse<SuspensionReason>(reason, ignoreCase: true, out var parsed)
            ? parsed
            : null;
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

    private sealed class TradingStateReadModelDto
    {
        public bool IsTradingSuspended { get; set; }
        public string? SuspensionReason { get; set; }
        public bool IsForcedSuspension { get; set; }
        public DateTimeOffset? SuspendedAt { get; set; }
        public DateTimeOffset? ResumedAt { get; set; }
        public DateTimeOffset? LastChangedAt { get; set; }
        public int OpenPositionsAtSuspension { get; set; }
        public int PendingOrdersAtSuspension { get; set; }
        public string? ChangedBy { get; set; }
        public string? Details { get; set; }
    }
}
