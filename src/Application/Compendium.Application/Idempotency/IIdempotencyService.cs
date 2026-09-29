using Compendium.Core.Results;

namespace Compendium.Application.Idempotency;

/// <summary>
/// Provides idempotency capabilities to ensure operations are executed only once.
/// Allows storing and retrieving operation results to prevent duplicate processing.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Checks if an operation has already been processed for the given idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with a boolean indicating if processed.</returns>
    Task<bool> IsProcessedAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the cached result for a previously processed operation.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the cached result or null if not found.</returns>
    Task<TResult?> GetResultAsync<TResult>(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the result of an operation for future idempotency checks.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="result">The result to store.</param>
    /// <param name="expiration">Optional custom expiration time. Uses default if not specified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetResultAsync<TResult>(string idempotencyKey, TResult result, TimeSpan? expiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an operation as processed without storing a specific result.
    /// </summary>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="expiration">Optional custom expiration time. Uses default if not specified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkAsProcessedAsync(string idempotencyKey, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of the idempotency service that uses an underlying store
/// to track processed operations and their results.
/// </summary>
public sealed class IdempotencyService : IIdempotencyService, IIdempotencyReservationService
{
    private readonly IIdempotencyStore _store;
    private readonly TimeSpan _defaultExpiration;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyService"/> class.
    /// </summary>
    /// <param name="store">The underlying store for idempotency data.</param>
    /// <param name="defaultExpiration">The default expiration time for idempotency records. Defaults to 24 hours.</param>
    /// <exception cref="ArgumentNullException">Thrown when store is null.</exception>
    public IdempotencyService(IIdempotencyStore store, TimeSpan? defaultExpiration = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromHours(24);
    }

    /// <summary>
    /// Checks if an operation has already been processed for the given idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with a boolean indicating if processed.</returns>
    /// <exception cref="ArgumentException">Thrown when idempotencyKey is null or empty.</exception>
    public async Task<bool> IsProcessedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(idempotencyKey));
        }

        var result = await _store.ExistsAsync(idempotencyKey, cancellationToken);

        // On store failure, assume key does not exist to allow the operation to proceed (graceful degradation).
        return result.IsSuccess && result.Value;
    }

    /// <summary>
    /// Retrieves the cached result for a previously processed operation.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the cached result or null if not found.</returns>
    /// <exception cref="ArgumentException">Thrown when idempotencyKey is null or empty.</exception>
    public async Task<TResult?> GetResultAsync<TResult>(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(idempotencyKey));
        }

        var result = await _store.GetAsync<TResult>(idempotencyKey, cancellationToken);

        // On store failure, return default to allow operation to proceed (graceful degradation).
        return result.IsSuccess ? result.Value : default;
    }

    /// <summary>
    /// Stores the result of an operation for future idempotency checks.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="result">The result to store.</param>
    /// <param name="expiration">Optional custom expiration time. Uses default if not specified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when idempotencyKey is null or empty.</exception>
    public async Task SetResultAsync<TResult>(string idempotencyKey, TResult result, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(idempotencyKey));
        }

        var storeResult = await _store.SetAsync(idempotencyKey, result, expiration ?? _defaultExpiration, cancellationToken);

        // Surface failures via an exception so callers (e.g. IdempotencyBehavior) can log them as best-effort.
        if (storeResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to persist idempotency record: {storeResult.Error.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> TryReserveAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(idempotencyKey));
        }

        if (_store is not IIdempotencyReservationStore reservations)
        {
            return Result.Failure<bool>(Error.Failure(
                "Idempotency.ReservationUnsupported",
                $"{_store.GetType().Name} does not implement {nameof(IIdempotencyReservationStore)}; "
                + "concurrent requests sharing a key cannot be serialised."));
        }

        // Same lifetime as the results: see IIdempotencyReservationService.
        return await reservations.TryReserveAsync(ReservationKeyFor(idempotencyKey), _defaultExpiration, cancellationToken);
    }

    /// <summary>
    /// The reservation lives beside the result, never on the same key: the result slot is
    /// how a replay tells "finished" from "still running".
    /// </summary>
    internal static string ReservationKeyFor(string idempotencyKey) => idempotencyKey + ":reservation";

    /// <summary>
    /// Marks an operation as processed without storing a specific result.
    /// </summary>
    /// <param name="idempotencyKey">The unique key identifying the operation.</param>
    /// <param name="expiration">Optional custom expiration time. Uses default if not specified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentException">Thrown when idempotencyKey is null or empty.</exception>
    public async Task MarkAsProcessedAsync(string idempotencyKey, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key cannot be null or empty", nameof(idempotencyKey));
        }

        var storeResult = await _store.SetAsync(idempotencyKey, true, expiration ?? _defaultExpiration, cancellationToken);

        // Surface failures via an exception so callers can log them as best-effort.
        if (storeResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to mark idempotency key as processed: {storeResult.Error.Message}");
        }
    }
}

/// <summary>
/// Defines the storage contract for idempotency data.
/// Implementations should provide persistent storage for operation tracking.
/// </summary>
/// <remarks>
/// All methods return <see cref="Result"/> / <see cref="Result{T}"/> so that infrastructure
/// failures (Redis connection, serialization, timeouts) are surfaced as structured errors
/// rather than exceptions. This aligns with the Compendium Result pattern.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Checks if a key exists in the store.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Result{Boolean}"/> indicating existence, or a failure on store error.</returns>
    Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a value from the store by key.
    /// </summary>
    /// <typeparam name="TResult">The type of the stored value.</typeparam>
    /// <param name="key">The key to retrieve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Result{TResult}"/> with the value or default when not found, or a failure on store error.</returns>
    Task<Result<TResult?>> GetAsync<TResult>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value in the store with the specified expiration.
    /// </summary>
    /// <typeparam name="TValue">The type of the value to store.</typeparam>
    /// <param name="key">The key for the value.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="expiration">The expiration time for the stored value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Result"/> indicating success or a failure on store error.</returns>
    Task<Result> SetAsync<TValue>(string key, TValue value, TimeSpan expiration, CancellationToken cancellationToken = default);
}
