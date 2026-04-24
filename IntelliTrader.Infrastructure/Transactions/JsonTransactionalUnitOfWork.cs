using System.Collections.Concurrent;
using System.Text;
using IntelliTrader.Application.Common;

namespace IntelliTrader.Infrastructure.Transactions;

/// <summary>
/// Transactional unit of work for JSON-backed repositories.
/// Repositories stage writes in-memory while a transaction is active,
/// and the unit of work commits them together.
/// </summary>
public sealed class JsonTransactionalUnitOfWork : ITransactionalUnitOfWork
{
    private readonly JsonTransactionCoordinator _coordinator;

    public JsonTransactionalUnitOfWork(JsonTransactionCoordinator? coordinator = null)
    {
        _coordinator = coordinator ?? JsonTransactionCoordinator.Shared;
    }

    public bool HasActiveTransaction => _coordinator.HasActiveTransaction;

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _coordinator.BeginTransaction();
        return Task.CompletedTask;
    }

    public Task<Result> CommitAsync(CancellationToken cancellationToken = default)
        => _coordinator.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default)
        => _coordinator.RollbackAsync(cancellationToken);
}

public sealed class JsonTransactionCoordinator
{
    public static JsonTransactionCoordinator Shared { get; } = new();

    private readonly AsyncLocal<JsonTransactionHolder?> _currentTransaction = new();
    private readonly SemaphoreSlim _commitLock = new(1, 1);

    public bool HasActiveTransaction => CurrentTransaction is not null;

    private JsonTransaction? CurrentTransaction
    {
        get => _currentTransaction.Value?.Transaction;
        set
        {
            var holder = _currentTransaction.Value;
            if (holder is null)
            {
                if (value is null)
                {
                    return;
                }

                holder = new JsonTransactionHolder();
                _currentTransaction.Value = holder;
            }

            holder.Transaction = value;
        }
    }

    public void BeginTransaction()
    {
        CurrentTransaction ??= new JsonTransaction();
    }

    internal TState GetOrCreateState<TState>(
        IJsonTransactionalResource resource,
        Func<TState> stateFactory)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(stateFactory);

        BeginTransaction();
        return CurrentTransaction!.GetOrCreateState(resource, stateFactory);
    }

    internal bool TryGetState<TState>(
        IJsonTransactionalResource resource,
        out TState? state)
        where TState : class
    {
        state = null;
        return CurrentTransaction is not null &&
               CurrentTransaction.TryGetState(resource, out state);
    }

    public async Task<Result> CommitAsync(CancellationToken cancellationToken = default)
    {
        var transaction = CurrentTransaction;
        if (transaction is null)
        {
            return Result.Success();
        }

        CurrentTransaction = null;
        await _commitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var preparedWrites = new List<JsonPreparedWrite>();

        try
        {
            foreach (var resource in transaction.EnlistedResources)
            {
                var preparedWrite = await resource
                    .PrepareCommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);

                if (preparedWrite is not null)
                {
                    preparedWrites.Add(preparedWrite);
                }
            }

            foreach (var preparedWrite in preparedWrites)
            {
                await preparedWrite.StageAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var preparedWrite in preparedWrites)
            {
                preparedWrite.Apply();
            }

            foreach (var resource in transaction.EnlistedResources)
            {
                await resource.AcceptCommitAsync(transaction, cancellationToken).ConfigureAwait(false);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            foreach (var preparedWrite in preparedWrites.AsEnumerable().Reverse())
            {
                preparedWrite.Restore();
            }

            foreach (var resource in transaction.EnlistedResources)
            {
                await resource.RollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            }

            return Result.Failure(
                Error.ExchangeError($"Failed to commit JSON transaction: {ex.Message}"));
        }
        finally
        {
            foreach (var preparedWrite in preparedWrites)
            {
                preparedWrite.Cleanup();
            }

            _commitLock.Release();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var transaction = CurrentTransaction;
        if (transaction is null)
        {
            return;
        }

        CurrentTransaction = null;
        foreach (var resource in transaction.EnlistedResources)
        {
            await resource.RollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class JsonTransactionHolder
    {
        public JsonTransaction? Transaction { get; set; }
    }
}

internal sealed class JsonTransaction
{
    private readonly ConcurrentDictionary<IJsonTransactionalResource, object> _states = new();

    public IReadOnlyCollection<IJsonTransactionalResource> EnlistedResources => _states.Keys.ToList();

    public TState GetOrCreateState<TState>(
        IJsonTransactionalResource resource,
        Func<TState> stateFactory)
        where TState : class
    {
        return (TState)_states.GetOrAdd(resource, _ => stateFactory());
    }

    public bool TryGetState<TState>(
        IJsonTransactionalResource resource,
        out TState? state)
        where TState : class
    {
        state = null;
        if (!_states.TryGetValue(resource, out var current))
        {
            return false;
        }

        state = current as TState;
        return state is not null;
    }
}

internal interface IJsonTransactionalResource
{
    Task<JsonPreparedWrite?> PrepareCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken);

    Task AcceptCommitAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken);

    Task RollbackAsync(
        JsonTransaction transaction,
        CancellationToken cancellationToken);
}

internal sealed class JsonPreparedWrite
{
    private readonly string _content;
    private string? _stagedPath;
    private string? _backupPath;
    private bool _applied;

    public JsonPreparedWrite(string targetPath, string content)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path cannot be null or empty.", nameof(targetPath));

        TargetPath = targetPath;
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public string TargetPath { get; }

    public async Task StageAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(TargetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stagingDirectory = string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
        _stagedPath = Path.Combine(
            stagingDirectory,
            $".{Path.GetFileName(TargetPath)}.{Guid.NewGuid():N}.tmp");

        await File.WriteAllTextAsync(_stagedPath, _content, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Apply()
    {
        if (string.IsNullOrWhiteSpace(_stagedPath))
        {
            throw new InvalidOperationException("Prepared write has not been staged.");
        }

        if (File.Exists(TargetPath))
        {
            _backupPath = $"{TargetPath}.{Guid.NewGuid():N}.bak";
            File.Copy(TargetPath, _backupPath, overwrite: true);
        }

        File.Move(_stagedPath, TargetPath, overwrite: true);
        _stagedPath = null;
        _applied = true;
    }

    public void Restore()
    {
        if (!_applied)
        {
            if (!string.IsNullOrWhiteSpace(_stagedPath) && File.Exists(_stagedPath))
            {
                File.Delete(_stagedPath);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(_backupPath) && File.Exists(_backupPath))
        {
            File.Move(_backupPath, TargetPath, overwrite: true);
            _backupPath = null;
        }
        else if (File.Exists(TargetPath))
        {
            File.Delete(TargetPath);
        }

        if (!string.IsNullOrWhiteSpace(_stagedPath) && File.Exists(_stagedPath))
        {
            File.Delete(_stagedPath);
        }
    }

    public void Cleanup()
    {
        if (!string.IsNullOrWhiteSpace(_backupPath) && File.Exists(_backupPath))
        {
            File.Delete(_backupPath);
        }

        if (!string.IsNullOrWhiteSpace(_stagedPath) && File.Exists(_stagedPath))
        {
            File.Delete(_stagedPath);
        }
    }
}
