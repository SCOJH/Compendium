// -----------------------------------------------------------------------
// <copyright file="IdempotencyBehavior.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Compendium.Application.Idempotency;
using Compendium.Core.Results;
using Microsoft.Extensions.Logging;

namespace Compendium.Application.CQRS.Behaviors;

/// <summary>
/// Pipeline behavior that provides idempotency for command processing.
/// Prevents duplicate command execution by caching results and checking for previously processed commands.
/// </summary>
/// <remarks>
/// <para>
/// Two keying modes. A command implementing <see cref="IIdempotentRequest"/> is keyed by the
/// caller's <see cref="IIdempotentRequest.IdempotencyKey"/>; a blank key disables
/// deduplication for that call. Any other command keeps the historical content-hash key.
/// </para>
/// <para>
/// For caller-keyed commands, when the service implements
/// <see cref="IIdempotencyReservationService"/> the key is reserved atomically before the
/// handler runs, and the reservation is never released: the winner records its response,
/// failure included, and every replay returns that response. A replay arriving while the
/// winner still runs waits up to <see cref="ReplayWait"/>, then gets an
/// <c>Idempotency.InProgress</c> conflict — never a second execution. A handler that throws
/// records nothing, so its key answers "in progress" until the reservation expires: the
/// side effects it may have produced are exactly why the operation is not re-run.
/// </para>
/// <para>
/// Keys are partitioned by <see cref="IIdempotentRequest.IdempotencyScope"/>. The request's
/// content is fingerprinted: the same key sent with different parameters is refused with
/// <c>Idempotency.KeyReused</c> instead of silently receiving another request's response.
/// If the store fails while reserving, the request is refused with
/// <c>Idempotency.Unavailable</c> rather than run beside a possible winner; only a service or
/// store that cannot reserve at all falls back to check-then-act.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The type of the request being processed.</typeparam>
/// <typeparam name="TResponse">The type of the response being returned.</typeparam>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="idempotencyService">The idempotency service for tracking processed operations.</param>
    /// <param name="logger">The logger instance.</param>
    public IdempotencyBehavior(
        IIdempotencyService idempotencyService,
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger)
    {
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Gets how long a replay waits for a concurrent winner to record its response before
    /// answering <c>Idempotency.InProgress</c>. Defaults to five seconds.
    /// </summary>
    public TimeSpan ReplayWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Handles the request with idempotency checking and result caching.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="next">The next handler in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the response.</returns>
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        // Only apply idempotency to commands, not queries
        if (!IsCommand(request))
        {
            return await next().ConfigureAwait(false);
        }

        if (request is IIdempotentRequest keyed)
        {
            return await HandleCallerKeyedAsync(request, keyed, next, cancellationToken).ConfigureAwait(false);
        }

        var idempotencyKey = GenerateIdempotencyKey(request);
        var requestName = typeof(TRequest).Name;

        _logger.LogDebug("Checking idempotency for {RequestName} with key {IdempotencyKey}", requestName, idempotencyKey);

        // Check if this command has already been processed
        var isProcessed = await _idempotencyService.IsProcessedAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);

        if (isProcessed)
        {
            _logger.LogInformation("Command {RequestName} with key {IdempotencyKey} has already been processed, returning cached result", requestName, idempotencyKey);

            // Return cached result
            var cachedResult = await _idempotencyService.GetResultAsync<TResponse>(idempotencyKey, cancellationToken).ConfigureAwait(false);

            if (cachedResult != null)
            {
                return cachedResult;
            }

            // If we can't retrieve the cached result, log warning and proceed
            _logger.LogWarning("Command {RequestName} was marked as processed but cached result not found for key {IdempotencyKey}", requestName, idempotencyKey);
        }

        // Process the command
        _logger.LogDebug("Processing {RequestName} with key {IdempotencyKey}", requestName, idempotencyKey);

        var response = await next().ConfigureAwait(false);

        // Cache the result for future idempotency checks
        try
        {
            await _idempotencyService.SetResultAsync(idempotencyKey, response, null, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Cached result for {RequestName} with key {IdempotencyKey}", requestName, idempotencyKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache result for {RequestName} with key {IdempotencyKey}", requestName, idempotencyKey);
            // Don't fail the request if caching fails, just log the error
        }

        return response;
    }

    private async Task<TResponse> HandleCallerKeyedAsync(
        TRequest request,
        IIdempotentRequest keyed,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        if (string.IsNullOrWhiteSpace(keyed.IdempotencyKey))
        {
            _logger.LogDebug("{RequestName} carries no idempotency key; running without deduplication", requestName);
            return await next().ConfigureAwait(false);
        }

        var key = CallerKeyFor(keyed.IdempotencyScope, keyed.IdempotencyKey);
        var fingerprintKey = key + ":fingerprint";
        var fingerprint = GenerateIdempotencyKey(request);

        if (_idempotencyService is IIdempotencyReservationService reservations)
        {
            var reserved = await reservations.TryReserveAsync(key, cancellationToken).ConfigureAwait(false);

            if (reserved.IsFailure && reserved.Error.Code != "Idempotency.ReservationUnsupported")
            {
                // The store could not answer. Falling back to check-then-act here would let
                // this call run beside a winner that holds the reservation — the one thing a
                // keyed request asked us never to do. Fail closed: the caller retries with the
                // same key and loses nothing.
                _logger.LogWarning(
                    "Could not reserve the idempotency key of {RequestName} ({ErrorCode}: {ErrorMessage}); refusing to run",
                    requestName, reserved.Error.Code, reserved.Error.Message);
                return Fail(Error.Unavailable(
                    "Idempotency.Unavailable",
                    $"The idempotency store could not reserve this key ({requestName}). Retry with the same key."));
            }

            if (reserved.IsSuccess)
            {
                if (!reserved.Value)
                {
                    return await ReplayAsync(key, fingerprintKey, fingerprint, requestName, cancellationToken).ConfigureAwait(false);
                }

                // Winning only proves no live reservation exists. The result of an earlier
                // winner can outlive its reservation (it was recorded after the handler ran),
                // and running again under the same key would be a second execution.
                var earlier = await _idempotencyService.GetResultAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
                if (earlier != null)
                {
                    return await ReturnRecordedAsync(earlier, fingerprintKey, fingerprint, requestName, cancellationToken).ConfigureAwait(false);
                }

                return await RunAndRecordAsync(key, fingerprintKey, fingerprint, requestName, next).ConfigureAwait(false);
            }

            _logger.LogWarning(
                "{ServiceType} cannot reserve keys; {RequestName} falls back to check-then-act, concurrent duplicates are possible",
                _idempotencyService.GetType().Name, requestName);
        }

        if (await _idempotencyService.IsProcessedAsync(key, cancellationToken).ConfigureAwait(false))
        {
            var cached = await _idempotencyService.GetResultAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                return await ReturnRecordedAsync(cached, fingerprintKey, fingerprint, requestName, cancellationToken).ConfigureAwait(false);
            }
        }

        return await RunAndRecordAsync(key, fingerprintKey, fingerprint, requestName, next).ConfigureAwait(false);
    }

    private async Task<TResponse> RunAndRecordAsync(
        string key,
        string fingerprintKey,
        string fingerprint,
        string requestName,
        RequestHandlerDelegate<TResponse> next)
    {
        // Recording is not cancelled with the request: a response computed but not recorded
        // would leave its key answering "in progress" until the reservation expires, even
        // though the operation succeeded.
        await TryRecordAsync(fingerprintKey, fingerprint, requestName).ConfigureAwait(false);

        var response = await next().ConfigureAwait(false);

        await TryRecordAsync(key, response, requestName).ConfigureAwait(false);
        await TryRecordAsync(fingerprintKey, fingerprint, requestName).ConfigureAwait(false);

        return response;
    }

    private async Task TryRecordAsync<TValue>(string key, TValue value, string requestName)
    {
        try
        {
            await _idempotencyService.SetResultAsync(key, value, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record idempotency data for {RequestName}; its replays may answer in progress", requestName);
        }
    }

    private async Task<TResponse> ReplayAsync(
        string key,
        string fingerprintKey,
        string fingerprint,
        string requestName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReplayWait;
        var delay = TimeSpan.FromMilliseconds(25);

        while (true)
        {
            // The fingerprint is written before the winner runs, so a reused key is refused
            // at once instead of after waiting for a response that is not this caller's.
            var reused = await KeyReusedAsync(fingerprintKey, fingerprint, cancellationToken).ConfigureAwait(false);
            if (reused)
            {
                return KeyReusedFailure(requestName);
            }

            var recorded = await _idempotencyService.GetResultAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
            if (recorded != null)
            {
                _logger.LogInformation("{RequestName} replayed: returning the recorded response", requestName);
                return recorded;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(delay < remaining ? delay : remaining, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 500));
        }

        return Fail(Error.Conflict(
            "Idempotency.InProgress",
            $"A request with the same idempotency key is still being processed ({requestName}). Retry later with the same key."));
    }

    private async Task<TResponse> ReturnRecordedAsync(
        TResponse recorded,
        string fingerprintKey,
        string fingerprint,
        string requestName,
        CancellationToken cancellationToken)
    {
        if (await KeyReusedAsync(fingerprintKey, fingerprint, cancellationToken).ConfigureAwait(false))
        {
            return KeyReusedFailure(requestName);
        }

        _logger.LogInformation("{RequestName} replayed: returning the recorded response", requestName);
        return recorded;
    }

    private async Task<bool> KeyReusedAsync(string fingerprintKey, string fingerprint, CancellationToken cancellationToken)
    {
        var recorded = await _idempotencyService.GetResultAsync<string>(fingerprintKey, cancellationToken).ConfigureAwait(false);
        return recorded != null && !string.Equals(recorded, fingerprint, StringComparison.Ordinal);
    }

    private TResponse KeyReusedFailure(string requestName)
    {
        _logger.LogWarning("{RequestName}: idempotency key reused with different parameters; refused", requestName);
        return Fail(Error.Conflict(
            "Idempotency.KeyReused",
            $"This idempotency key was already used for a {requestName} with different parameters. Use a new key."));
    }

    /// <summary>
    /// A fixed-shape key: the request type, then a SHA-256 of the scope and the caller key.
    /// Hashing bounds the length and the alphabet of whatever the caller sent, and — because
    /// the result key always ends in hex — no caller key can land on the <c>:reservation</c>
    /// or <c>:fingerprint</c> slot of another. Scope and key are length-prefixed so that
    /// ("ab", "c") and ("a", "bc") cannot hash alike.
    /// </summary>
    private static string CallerKeyFor(string? scope, string callerKey)
    {
        var material = $"{scope?.Length ?? -1}:{scope}|{callerKey.Length}:{callerKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return $"idempotency:request:{typeof(TRequest).FullName}:{hash}";
    }

    private static TResponse Fail(Error error)
        => FailureAs(error)
           ?? throw new InvalidOperationException(
               $"{error.Message} {typeof(TResponse).Name} is not a Result type, so the refusal cannot be returned as a value.");

    /// <summary>
    /// Builds <typeparamref name="TResponse"/> as a failure when it is <see cref="Result"/> or
    /// <see cref="Result{TValue}"/>; <c>null</c> otherwise. Same construction as
    /// <see cref="ValidationBehavior{TRequest, TResponse}"/>.
    /// </summary>
    private static TResponse? FailureAs(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(error);
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failure = typeof(Result)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Single(m => m.Name == nameof(Result.Failure)
                          && m.IsGenericMethodDefinition
                          && m.GetParameters().Length == 1
                          && m.GetParameters()[0].ParameterType == typeof(Error))
                .MakeGenericMethod(typeof(TResponse).GetGenericArguments()[0]);

            return (TResponse)failure.Invoke(null, [error])!;
        }

        return null;
    }

    /// <summary>
    /// Determines if the request is a command that should have idempotency applied.
    /// </summary>
    /// <param name="request">The request to check.</param>
    /// <returns>True if the request is a command; otherwise, false.</returns>
    private static bool IsCommand(TRequest request)
    {
        var requestType = request.GetType();

        // Check for ICommand interface
        return requestType.GetInterfaces().Any(i =>
            i.Name == "ICommand" ||
            (i.IsGenericType && i.GetGenericTypeDefinition().Name == "ICommand`1"));
    }

    /// <summary>
    /// Generates a unique idempotency key based on the request content and type.
    /// </summary>
    /// <param name="request">The request to generate a key for.</param>
    /// <returns>A unique idempotency key.</returns>
    private string GenerateIdempotencyKey(TRequest request)
    {
        try
        {
            // Serialize the request to JSON for consistent hashing
            var requestJson = JsonSerializer.Serialize(request, _jsonOptions);
            var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;

            // Combine type name and serialized content
            var combinedContent = $"{requestType}:{requestJson}";

            // Generate SHA256 hash for the key
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combinedContent));
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return $"idempotency:{hash}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate idempotency key for {RequestType}, falling back to type-based key", typeof(TRequest).Name);

            // Fallback: use type name and a simple hash of the request
            var fallbackContent = $"{typeof(TRequest).Name}:{request.GetHashCode()}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fallbackContent));
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return $"idempotency:fallback:{hash}";
        }
    }
}
