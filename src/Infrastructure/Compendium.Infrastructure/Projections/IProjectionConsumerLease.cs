// -----------------------------------------------------------------------
// <copyright file="IProjectionConsumerLease.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Infrastructure.Projections;

/// <summary>
/// Port for the right to be the <b>single</b> process applying events to projections.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LiveProjectionProcessor"/> is a <c>BackgroundService</c>: every replica of
/// a host that registers it runs one. Without a cluster-wide claim, each replica applies
/// every event to every projection, and the replica that restarts furthest behind writes
/// a <i>lower</i> checkpoint and replays an interval already applied. An in-process
/// semaphore cannot see the other pods, so it does not help here.
/// </para>
/// <para>
/// This is a port on purpose. A Postgres advisory lock is the natural implementation, but
/// this assembly has no database dependency and must not acquire one: the adapter lives
/// with the database driver, on the consumer's side. The default implementation
/// (<see cref="SingleProcessProjectionConsumerLease"/>) always grants the lease, which
/// preserves single-process hosts and unit tests unchanged.
/// </para>
/// </remarks>
public interface IProjectionConsumerLease
{
    /// <summary>
    /// Attempts to take the lease without blocking.
    /// </summary>
    /// <param name="consumerName">
    /// Identifies the consumer group. Every process that must exclude the others uses the
    /// same name; distinct names are independent leases.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A handle while the lease is held, or <see langword="null"/> when another process
    /// holds it. The caller disposes the handle to release.
    /// </returns>
    Task<IProjectionConsumerLeaseHandle?> TryAcquireAsync(
        string consumerName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A held projection-consumer lease. Disposing releases it.
/// </summary>
/// <remarks>
/// An implementation must make release a property of the mechanism rather than of the
/// holder's error handling: if the process disappears without disposing — a killed pod, a
/// severed network — the lease has to become available to someone else on its own. A
/// server-side lock bound to a connection does that; a row holding a "leader" flag does
/// not, unless it also carries an expiry.
/// </remarks>
public interface IProjectionConsumerLeaseHandle : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the lease is still held, as far as the last
    /// <see cref="RenewAsync"/> could tell.
    /// </summary>
    bool IsHeld { get; }

    /// <summary>
    /// Confirms the lease is still ours, extending it where the implementation expires.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> while the lease is held; <see langword="false"/> once it is
    /// lost, at which point the holder must stop applying events.
    /// </returns>
    Task<bool> RenewAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default lease: this process is always the leader.
/// </summary>
/// <remarks>
/// Correct for a single-process host and for tests. In a multi-replica deployment it
/// grants the lease to every replica at once, which is exactly the situation the port
/// exists to end — such a deployment must register a real implementation.
/// </remarks>
public sealed class SingleProcessProjectionConsumerLease : IProjectionConsumerLease
{
    /// <inheritdoc />
    public Task<IProjectionConsumerLeaseHandle?> TryAcquireAsync(
        string consumerName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IProjectionConsumerLeaseHandle?>(new Handle());

    private sealed class Handle : IProjectionConsumerLeaseHandle
    {
        public bool IsHeld => true;

        public Task<bool> RenewAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
