// -----------------------------------------------------------------------
// <copyright file="CommandDispatcherTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Application.CQRS;
using Compendium.Application.CQRS.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Compendium.Application.Tests.CQRS;

/// <summary>
/// Unit tests for the <see cref="CommandDispatcher"/> class.
/// </summary>
public class CommandDispatcherTests
{
    public sealed class TestCommand : ICommand
    {
        public string? Payload { get; init; }
    }

    public sealed class TestCommandWithResult : ICommand<int>
    {
        public int Value { get; init; }
    }

    [Fact]
    public void Constructor_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => new CommandDispatcher(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public async Task DispatchAsync_WhenCommandIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommand>(null!, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.Null");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task DispatchAsync_WhenNoHandlerRegistered_ReturnsNotFoundFailure()
    {
        // Arrange
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Handler.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Message.Should().Contain(nameof(TestCommand));
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerSucceeds_ReturnsSuccess()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await handler.Received(1).HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerReturnsFailure_ReturnsHandlerFailure()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(Error.Failure("Test.Error", "boom"))));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Test.Error");
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrows_ReturnsExecutionFailedFailure()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("kaboom"));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.ExecutionFailed");
        result.Error.Message.Should().Contain("kaboom");
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerThrows_LogsExceptionBeforeWrappingInFailure()
    {
        // Arrange
        var thrown = new InvalidOperationException("kaboom");
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw thrown);

        var logger = new CapturingLogger<CommandDispatcher>();
        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton<ILogger<CommandDispatcher>>(logger)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert — the Result contract is preserved (P0-02: never rethrow)
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.ExecutionFailed");

        // Assert — the exception was logged with its stack trace, not silently swallowed
        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.Exception.Should().BeSameAs(thrown);
        errorEntry.Message.Should().Contain(nameof(TestCommand));

        // Assert — the exception type is preserved in the error metadata (non-breaking enrichment)
        result.Error.Metadata.Should().ContainKey("exceptionType");
        result.Error.Metadata["exceptionType"].Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task DispatchAsync_WhenNoLoggerRegistered_StillReturnsFailureWithoutThrowing()
    {
        // Arrange — no ILogger registered; dispatcher must fall back to NullLogger and not throw
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("kaboom"));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.ExecutionFailed");
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenHandlerThrows_LogsExceptionBeforeWrappingInFailure()
    {
        // Arrange
        var thrown = new InvalidOperationException("oops");
        var handler = Substitute.For<ICommandHandler<TestCommandWithResult, int>>();
        handler.HandleAsync(Arg.Any<TestCommandWithResult>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<int>>>(_ => throw thrown);

        var logger = new CapturingLogger<CommandDispatcher>();
        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton<ILogger<CommandDispatcher>>(logger)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(
            new TestCommandWithResult { Value = 1 },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.ExecutionFailed");

        var errorEntry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        errorEntry.Exception.Should().BeSameAs(thrown);
        errorEntry.Message.Should().Contain(nameof(TestCommandWithResult));

        result.Error.Metadata.Should().ContainKey("exceptionType");
        result.Error.Metadata["exceptionType"].Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task DispatchAsync_WhenHandlerSucceeds_DoesNotLogError()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var logger = new CapturingLogger<CommandDispatcher>();
        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .AddSingleton<ILogger<CommandDispatcher>>(logger)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task DispatchAsync_WhenBehaviorsRegistered_InvokesPipelineInRegistrationOrder()
    {
        // Arrange
        var calls = new List<string>();

        var b1 = new RecordingBehavior<TestCommand, Result>("b1", calls);
        var b2 = new RecordingBehavior<TestCommand, Result>("b2", calls);

        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("handler");
                return Task.FromResult(Result.Success());
            });

        var sp = new ServiceCollection()
            .AddSingleton<IPipelineBehavior<TestCommand, Result>>(b1)
            .AddSingleton<IPipelineBehavior<TestCommand, Result>>(b2)
            .AddSingleton(handler)
            .BuildServiceProvider();

        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync(new TestCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // The pipeline reverses behaviours so the first registered runs first.
        calls.Should().Equal("b1", "b2", "handler");
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenCommandIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(null!, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.Null");
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenNoHandlerRegistered_ReturnsNotFoundFailure()
    {
        // Arrange
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(new TestCommandWithResult(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Handler.NotFound");
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenHandlerSucceeds_ReturnsHandlerValue()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommandWithResult, int>>();
        handler.HandleAsync(Arg.Any<TestCommandWithResult>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(42)));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(
            new TestCommandWithResult { Value = 1 },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenHandlerReturnsFailure_ReturnsFailure()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommandWithResult, int>>();
        handler.HandleAsync(Arg.Any<TestCommandWithResult>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<int>(Error.Failure("X", "y"))));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(
            new TestCommandWithResult { Value = 1 },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("X");
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenHandlerThrows_ReturnsExecutionFailedFailure()
    {
        // Arrange
        var handler = Substitute.For<ICommandHandler<TestCommandWithResult, int>>();
        handler.HandleAsync(Arg.Any<TestCommandWithResult>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<int>>>(_ => throw new InvalidOperationException("oops"));

        var sp = new ServiceCollection()
            .AddSingleton(handler)
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(
            new TestCommandWithResult { Value = 1 },
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Command.ExecutionFailed");
        result.Error.Message.Should().Contain("oops");
    }

    [Fact]
    public async Task DispatchAsyncTResult_WhenBehaviorsRegistered_InvokesPipelineInRegistrationOrder()
    {
        // Arrange
        var calls = new List<string>();

        var b1 = new RecordingBehavior<TestCommandWithResult, Result<int>>("b1", calls);
        var b2 = new RecordingBehavior<TestCommandWithResult, Result<int>>("b2", calls);

        var handler = Substitute.For<ICommandHandler<TestCommandWithResult, int>>();
        handler.HandleAsync(Arg.Any<TestCommandWithResult>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("handler");
                return Task.FromResult(Result.Success(99));
            });

        var sp = new ServiceCollection()
            .AddSingleton<IPipelineBehavior<TestCommandWithResult, Result<int>>>(b1)
            .AddSingleton<IPipelineBehavior<TestCommandWithResult, Result<int>>>(b2)
            .AddSingleton(handler)
            .BuildServiceProvider();

        var dispatcher = new CommandDispatcher(sp);

        // Act
        var result = await dispatcher.DispatchAsync<TestCommandWithResult, int>(
            new TestCommandWithResult { Value = 1 },
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(99);
        calls.Should().Equal("b1", "b2", "handler");
    }

    private sealed class RecordingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public RecordingBehavior(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            _calls.Add(_name);
            return await next();
        }
    }
}
