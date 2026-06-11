using System.Diagnostics;
using Compendium.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Application.CQRS;

/// <summary>
/// Defines a dispatcher for executing queries through their corresponding handlers.
/// Provides a central entry point for query execution with error handling.
/// </summary>
public interface IQueryDispatcher
{
    /// <summary>
    /// Dispatches a query to retrieve data.
    /// </summary>
    /// <typeparam name="TQuery">The type of the query.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="query">The query to dispatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    Task<Result<TResult>> DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : class, IQuery<TResult>;
}

/// <summary>
/// Default implementation of the query dispatcher that uses dependency injection
/// to resolve query handlers and execute queries with proper error handling.
/// </summary>
public sealed class QueryDispatcher : IQueryDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueryDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryDispatcher"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving handlers.</param>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
    /// <remarks>
    /// P0-02: The logger is resolved from <paramref name="serviceProvider"/> rather than injected
    /// directly so that the existing public single-parameter constructor remains binary- and
    /// source-compatible for downstream consumers. When the container has no logging configured
    /// (e.g. in lightweight unit tests) a <see cref="NullLogger{T}"/> is used as a safe fallback.
    /// </remarks>
    public QueryDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = _serviceProvider.GetService<ILogger<QueryDispatcher>>() ?? NullLogger<QueryDispatcher>.Instance;
    }

    /// <summary>
    /// Dispatches a query to retrieve data through its registered handler.
    /// </summary>
    /// <typeparam name="TQuery">The type of the query.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="query">The query to dispatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    public async Task<Result<TResult>> DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : class, IQuery<TResult>
    {
        // COMP-019: Start distributed trace for query dispatch
        using var activity = CompendiumTelemetry.ActivitySource.StartActivity(
            CompendiumTelemetry.CqrsActivities.DispatchQuery);

        activity?.SetTag(CompendiumTelemetry.Tags.QueryType, typeof(TQuery).Name);

        if (query is null)
        {
            return Result.Failure<TResult>(Error.Validation("Query.Null", "Query cannot be null"));
        }

        var handler = _serviceProvider.GetService<IQueryHandler<TQuery, TResult>>();
        if (handler is null)
        {
            return Result.Failure<TResult>(Error.NotFound("Handler.NotFound", $"No handler found for query {typeof(TQuery).Name}"));
        }

        activity?.SetTag(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name);

        var sw = Stopwatch.StartNew();

        try
        {
            var result = await handler.HandleAsync(query, cancellationToken);
            sw.Stop();

            // COMP-019: Record metrics
            var status = result.IsSuccess
                ? CompendiumTelemetry.StatusValues.Success
                : CompendiumTelemetry.StatusValues.Failure;

            CompendiumTelemetry.QueriesDispatched.Add(1,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.QueryType, typeof(TQuery).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.Status, status));

            CompendiumTelemetry.QueryDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.QueryType, typeof(TQuery).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name));

            activity?.SetStatus(result.IsSuccess
                ? ActivityStatusCode.Ok
                : ActivityStatusCode.Error);

            if (!result.IsSuccess)
            {
                activity?.SetTag(CompendiumTelemetry.Tags.ErrorType, result.Error.Code);
                activity?.SetTag(CompendiumTelemetry.Tags.ErrorMessage, result.Error.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();

            // COMP-019: Record error metrics
            CompendiumTelemetry.QueriesDispatched.Add(1,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.QueryType, typeof(TQuery).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.Status, CompendiumTelemetry.StatusValues.Failure));

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            // P0-02: Log the swallowed exception with stack trace BEFORE wrapping it into a
            // Result.Failure. Without this, production debugging is blind — the exception is
            // converted to a failure Result and the only trace is on OTel spans.
            _logger.LogError(
                ex,
                "Query handler for {QueryType} returning {ResultType} threw an unhandled exception after {ElapsedMs}ms; converting to Result.Failure({ErrorCode})",
                typeof(TQuery).Name,
                typeof(TResult).Name,
                sw.Elapsed.TotalMilliseconds,
                "Query.ExecutionFailed");

            return Result.Failure<TResult>(Error.Failure(
                "Query.ExecutionFailed",
                ex.Message,
                BuildExceptionMetadata(ex)));
        }
    }

    /// <summary>
    /// Builds the error metadata dictionary attached to an execution-failure error, capturing the
    /// exception type so downstream consumers can discriminate the underlying failure cause without
    /// parsing the message string. P0-02.
    /// </summary>
    /// <param name="exception">The exception that was thrown by the handler.</param>
    /// <returns>A read-only metadata dictionary containing the exception type name.</returns>
    private static IReadOnlyDictionary<string, object> BuildExceptionMetadata(Exception exception)
    {
        return new Dictionary<string, object>
        {
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
        };
    }
}
