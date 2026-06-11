using System.Diagnostics;
using Compendium.Application.CQRS.Behaviors;
using Compendium.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Application.CQRS;

/// <summary>
/// Defines a dispatcher for executing commands through their corresponding handlers.
/// Provides a central entry point for command execution with error handling.
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// Dispatches a command that doesn't return a value.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    Task<Result> DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, ICommand;

    /// <summary>
    /// Dispatches a command that returns a value.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    Task<Result<TResult>> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, ICommand<TResult>;
}

/// <summary>
/// Default implementation of the command dispatcher that uses dependency injection
/// to resolve command handlers and execute commands with proper error handling and pipeline behaviors.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandDispatcher"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving handlers and behaviors.</param>
    /// <exception cref="ArgumentNullException">Thrown when serviceProvider is null.</exception>
    /// <remarks>
    /// P0-02: The logger is resolved from <paramref name="serviceProvider"/> rather than injected
    /// directly so that the existing public single-parameter constructor remains binary- and
    /// source-compatible for downstream consumers. When the container has no logging configured
    /// (e.g. in lightweight unit tests) a <see cref="NullLogger{T}"/> is used as a safe fallback.
    /// </remarks>
    public CommandDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = _serviceProvider.GetService<ILogger<CommandDispatcher>>() ?? NullLogger<CommandDispatcher>.Instance;
    }

    /// <summary>
    /// Dispatches a command that doesn't return a value through its registered handler and pipeline behaviors.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    public async Task<Result> DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, ICommand
    {
        // COMP-019: Start distributed trace for command dispatch
        using var activity = CompendiumTelemetry.ActivitySource.StartActivity(
            CompendiumTelemetry.CqrsActivities.DispatchCommand);

        activity?.SetTag(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name);

        if (command is null)
        {
            return Result.Failure(Error.Validation("Command.Null", "Command cannot be null"));
        }

        var handler = _serviceProvider.GetService<ICommandHandler<TCommand>>();
        if (handler is null)
        {
            return Result.Failure(Error.NotFound("Handler.NotFound", $"No handler found for command {typeof(TCommand).Name}"));
        }

        activity?.SetTag(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name);

        // Build the pipeline with behaviors
        RequestHandlerDelegate<Result> handlerDelegate = () => ExecuteHandlerAsync(handler, command, cancellationToken);

        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TCommand, Result>>().Reverse().ToList();
        activity?.SetTag(CompendiumTelemetry.Tags.BehaviorCount, behaviors.Count);

        foreach (var behavior in behaviors)
        {
            var currentHandler = handlerDelegate;
            handlerDelegate = () => behavior.HandleAsync(command, currentHandler, cancellationToken);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var result = await handlerDelegate().ConfigureAwait(false);
            sw.Stop();

            // COMP-019: Record metrics
            var status = result.IsSuccess
                ? CompendiumTelemetry.StatusValues.Success
                : CompendiumTelemetry.StatusValues.Failure;

            CompendiumTelemetry.CommandsDispatched.Add(1,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.Status, status));

            CompendiumTelemetry.CommandDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.BehaviorCount, behaviors.Count));

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
            CompendiumTelemetry.CommandsDispatched.Add(1,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.Status, CompendiumTelemetry.StatusValues.Failure));

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            // P0-02: Log the swallowed exception with stack trace BEFORE wrapping it into a
            // Result.Failure. Without this, production debugging is blind — the exception is
            // converted to a failure Result and the only trace is on OTel spans.
            _logger.LogError(
                ex,
                "Command handler for {CommandType} threw an unhandled exception after {ElapsedMs}ms; converting to Result.Failure({ErrorCode})",
                typeof(TCommand).Name,
                sw.Elapsed.TotalMilliseconds,
                "Command.ExecutionFailed");

            return Result.Failure(Error.Failure(
                "Command.ExecutionFailed",
                ex.Message,
                BuildExceptionMetadata(ex)));
        }
    }

    /// <summary>
    /// Dispatches a command that returns a value through its registered handler and pipeline behaviors.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="command">The command to dispatch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    public async Task<Result<TResult>> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : class, ICommand<TResult>
    {
        // COMP-019: Start distributed trace for command dispatch
        using var activity = CompendiumTelemetry.ActivitySource.StartActivity(
            CompendiumTelemetry.CqrsActivities.DispatchCommand);

        activity?.SetTag(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name);

        if (command is null)
        {
            return Result.Failure<TResult>(Error.Validation("Command.Null", "Command cannot be null"));
        }

        var handler = _serviceProvider.GetService<ICommandHandler<TCommand, TResult>>();
        if (handler is null)
        {
            return Result.Failure<TResult>(Error.NotFound("Handler.NotFound", $"No handler found for command {typeof(TCommand).Name}"));
        }

        activity?.SetTag(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name);

        // Build the pipeline with behaviors
        RequestHandlerDelegate<Result<TResult>> handlerDelegate = () => ExecuteHandlerAsync(handler, command, cancellationToken);

        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TCommand, Result<TResult>>>().Reverse().ToList();
        activity?.SetTag(CompendiumTelemetry.Tags.BehaviorCount, behaviors.Count);

        foreach (var behavior in behaviors)
        {
            var currentHandler = handlerDelegate;
            handlerDelegate = () => behavior.HandleAsync(command, currentHandler, cancellationToken);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var result = await handlerDelegate().ConfigureAwait(false);
            sw.Stop();

            // COMP-019: Record metrics
            var status = result.IsSuccess
                ? CompendiumTelemetry.StatusValues.Success
                : CompendiumTelemetry.StatusValues.Failure;

            CompendiumTelemetry.CommandsDispatched.Add(1,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.Status, status));

            CompendiumTelemetry.CommandDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.HandlerType, handler.GetType().Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.BehaviorCount, behaviors.Count));

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
            CompendiumTelemetry.CommandsDispatched.Add(1,
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.CommandType, typeof(TCommand).Name),
                new KeyValuePair<string, object?>(CompendiumTelemetry.Tags.Status, CompendiumTelemetry.StatusValues.Failure));

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            // P0-02: Log the swallowed exception with stack trace BEFORE wrapping it into a
            // Result.Failure. Without this, production debugging is blind — the exception is
            // converted to a failure Result and the only trace is on OTel spans.
            _logger.LogError(
                ex,
                "Command handler for {CommandType} returning {ResultType} threw an unhandled exception after {ElapsedMs}ms; converting to Result.Failure({ErrorCode})",
                typeof(TCommand).Name,
                typeof(TResult).Name,
                sw.Elapsed.TotalMilliseconds,
                "Command.ExecutionFailed");

            return Result.Failure<TResult>(Error.Failure(
                "Command.ExecutionFailed",
                ex.Message,
                BuildExceptionMetadata(ex)));
        }
    }

    /// <summary>
    /// Builds the error metadata dictionary attached to an execution-failure error, capturing the
    /// exception type so downstream consumers can discriminate the underlying failure cause without
    /// parsing the message string. P0-02.
    /// </summary>
    /// <param name="exception">The exception that was thrown by the handler pipeline.</param>
    /// <returns>A read-only metadata dictionary containing the exception type name.</returns>
    private static IReadOnlyDictionary<string, object> BuildExceptionMetadata(Exception exception)
    {
        return new Dictionary<string, object>
        {
            ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
        };
    }

    /// <summary>
    /// Executes the command handler for commands that don't return a value.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <param name="handler">The command handler.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    private static async Task<Result> ExecuteHandlerAsync<TCommand>(
        ICommandHandler<TCommand> handler,
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : class, ICommand
    {
        return await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command handler for commands that return a value.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="handler">The command handler.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the result.</returns>
    private static async Task<Result<TResult>> ExecuteHandlerAsync<TCommand, TResult>(
        ICommandHandler<TCommand, TResult> handler,
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : class, ICommand<TResult>
    {
        return await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
