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
            return await HandleCallerKeyedAsync(keyed.IdempotencyKey, next, cancellationToken).ConfigureAwait(false);
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
        string? callerKey,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        if (string.IsNullOrWhiteSpace(callerKey))
        {
            _logger.LogDebug("{RequestName} carries no idempotency key; running without deduplication", requestName);
            return await next().ConfigureAwait(false);
        }

        // Namespaced by request type: the same caller key on two different commands names
        // two different operations.
        var key = $"idempotency:request:{typeof(TRequest).FullName}:{callerKey}";

        if (_idempotencyService is IIdempotencyReservationService reservations)
        {
            var reserved = await reservations.TryReserveAsync(key, cancellationToken).ConfigureAwait(false);

            if (reserved.IsSuccess)
            {
                return reserved.Value
                    ? await RunAndRecordAsync(key, requestName, next, cancellationToken).ConfigureAwait(false)
                    : await ReplayAsync(key, requestName, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogWarning(
                "Could not reserve idempotency key for {RequestName} ({ErrorCode}: {ErrorMessage}); "
                + "falling back to check-then-act, concurrent duplicates are possible",
                requestName, reserved.Error.Code, reserved.Error.Message);
        }

        if (await _idempotencyService.IsProcessedAsync(key, cancellationToken).ConfigureAwait(false))
        {
            var cached = await _idempotencyService.GetResultAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);
            if (cached != null)
            {
                return cached;
            }
        }

        return await RunAndRecordAsync(key, requestName, next, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> RunAndRecordAsync(
        string key,
        string requestName,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        try
        {
            await _idempotencyService.SetResultAsync(key, response, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record the response of {RequestName}; its replays will answer in progress", requestName);
        }

        return response;
    }

    private async Task<TResponse> ReplayAsync(string key, string requestName, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReplayWait;
        var delay = TimeSpan.FromMilliseconds(25);

        while (true)
        {
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

        var error = Error.Conflict(
            "Idempotency.InProgress",
            $"A request with the same idempotency key is still being processed ({requestName}). Retry later with the same key.");

        return FailureAs(error)
            ?? throw new InvalidOperationException(
                $"{error.Message} {typeof(TResponse).Name} is not a Result type, so the conflict cannot be returned as a value.");
    }

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
