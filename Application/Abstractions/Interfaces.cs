using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
// Repository interfaces (Ports — implemented in Infrastructure)
// ─────────────────────────────────────────────────────────────────────────────

public interface IRepository<T> where T : Domain.BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
}

public interface IParticipantRepository : IRepository<Participant>
{
    Task<Participant?> GetByRtnAsync(string rtn, CancellationToken ct = default);
    Task<IReadOnlyList<Participant>> GetAllActiveAsync(CancellationToken ct = default);
}

public interface INachaFileRepository : IRepository<NachaFile>
{
    Task<NachaFile?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default);
}

public interface ITransactionRepository : IRepository<Transaction>
{
    /// <summary>Returns all pending transactions not yet assigned to a cycle.</summary>
    Task<IReadOnlyList<Transaction>> GetPendingAsync(int batchSize, CancellationToken ct = default);

    Task<IReadOnlyList<Transaction>> GetByCycleIdAsync(Guid cycleId, CancellationToken ct = default);
}

public interface ISettlementCycleRepository : IRepository<SettlementCycle>
{
    Task<SettlementCycle?> GetOpenCycleAsync(CancellationToken ct = default);
    Task<SettlementCycle?> GetByReferenceAsync(string reference, CancellationToken ct = default);
}

public interface IBilateralPositionRepository : IRepository<BilateralPosition>
{
    Task<BilateralPosition?> GetAsync(
        Guid cycleId, string debitingRtn, string creditingRtn, CancellationToken ct = default);

    Task<IReadOnlyList<BilateralPosition>> GetByCycleAsync(Guid cycleId, CancellationToken ct = default);
}

public interface ISettlementObligationRepository : IRepository<SettlementObligation>
{
    Task<IReadOnlyList<SettlementObligation>> GetByCycleAsync(Guid cycleId, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Unit of Work
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Wraps EF Core's DbContext SaveChangesAsync.
/// Dispatches domain events after the transaction commits.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Cache abstractions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Redis-backed cache for participant/RTN lookups.
/// Hot path in the ingestion service — every transaction requires an RTN validation.
/// </summary>
public interface IParticipantCache
{
    /// <summary>Returns participant info from cache, falling back to DB on miss.</summary>
    Task<CachedParticipant?> GetByRtnAsync(string rtn, CancellationToken ct = default);

    /// <summary>Explicitly evicts a participant on status change (e.g., suspension).</summary>
    Task InvalidateAsync(string rtn, CancellationToken ct = default);

    Task SetAsync(CachedParticipant participant, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Idempotency store abstraction
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Redis-backed idempotency store for NACHA file submissions.
/// Prevents duplicate processing if a bank re-submits the same file.
/// Uses Redis SET NX (set-if-not-exists) for atomic check-and-set.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically checks and reserves an idempotency key.
    /// Returns true if the key is new (first submission).
    /// Returns false if the key already exists (duplicate).
    /// </summary>
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Marks the key as permanently processed with its final status.
    /// Extends TTL to the full retention window.
    /// </summary>
    Task MarkProcessedAsync(string key, string status, CancellationToken ct = default);

    Task<string?> GetStatusAsync(string key, CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Message bus abstraction
// ─────────────────────────────────────────────────────────────────────────────

public interface IMessageProducer
{
    Task ProduceAsync<T>(string topic, string key, T message, CancellationToken ct = default)
        where T : class;
}

public interface IMessageConsumer<T> where T : class
{
    /// <summary>
    /// Subscribes to a topic and invokes the handler for each message.
    /// Handler must be idempotent — at-least-once delivery is guaranteed.
    /// </summary>
    Task ConsumeAsync(
        string topic,
        Func<T, CancellationToken, Task> handler,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// File storage abstraction
// ─────────────────────────────────────────────────────────────────────────────

public interface IFileStorage
{
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken ct = default);
}
