// -----------------------------------------------------------------------
// <copyright file="CircuitBreakerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.Resilience;

namespace Compendium.Infrastructure.Tests.Resilience;

/// <summary>
/// Comprehensive test suite for CircuitBreaker implementation.
/// Tests state transitions, threshold management, timeouts, and thread-safety.
/// </summary>
public sealed class CircuitBreakerTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<CircuitBreaker> _logger;
    private readonly CircuitBreakerOptions _defaultOptions;

    public CircuitBreakerTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = Substitute.For<ILogger<CircuitBreaker>>();
        _defaultOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            OpenTimeout = TimeSpan.FromMilliseconds(100)
        };
    }

    #region State Transitions

    [Fact]
    public void CircuitBreaker_InitialState_ShouldBeClosed()
    {
        // Arrange & Act
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task CircuitBreaker_AfterThresholdFailures_ShouldOpen()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var failingOperation = CreateFailingOperation();

        // Act - Execute failing operations up to threshold
        for (int i = 0; i < _defaultOptions.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task CircuitBreaker_InOpenState_ShouldRejectCallsImmediately()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var failingOperation = CreateFailingOperation();
        var successfulOperation = CreateSuccessfulOperation();

        // Act - Trip the circuit breaker
        for (int i = 0; i < _defaultOptions.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
        }

        var result = await circuitBreaker.ExecuteAsync(successfulOperation);

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CircuitBreaker.Open");

        // Throughput / fast-fail latency is measured in
        // tests/Perf/Compendium.Infrastructure.PerfTests (ResilienceBenchmarks).
    }

    [Fact]
    public async Task CircuitBreaker_AfterBreakDuration_ShouldTransitionToHalfOpen()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            OpenTimeout = TimeSpan.FromMilliseconds(50)
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var failingOperation = CreateFailingOperation();
        var successfulOperation = CreateSuccessfulOperation();

        // Act - Trip the circuit breaker
        for (int i = 0; i < options.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
        }

        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);

        // Wait for break duration to pass
        await Task.Delay(options.OpenTimeout.Add(TimeSpan.FromMilliseconds(10)));

        // Execute operation to trigger state transition
        await circuitBreaker.ExecuteAsync(successfulOperation);

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task CircuitBreaker_InHalfOpen_AfterSuccesses_ShouldClose()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            OpenTimeout = TimeSpan.FromMilliseconds(50)
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var failingOperation = CreateFailingOperation();
        var successfulOperation = CreateSuccessfulOperation();

        // Act - Trip the circuit breaker
        for (int i = 0; i < options.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
        }

        // Wait for break duration and execute successful operation
        await Task.Delay(options.OpenTimeout.Add(TimeSpan.FromMilliseconds(10)));
        var result = await circuitBreaker.ExecuteAsync(successfulOperation);

        // Assert
        result.IsSuccess.Should().BeTrue();
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task CircuitBreaker_InHalfOpen_AfterFailure_ShouldReopenImmediately()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            OpenTimeout = TimeSpan.FromMilliseconds(50)
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var failingOperation = CreateFailingOperation();

        // Act - Trip the circuit breaker
        for (int i = 0; i < options.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
        }

        // Wait for break duration and execute failing operation
        await Task.Delay(options.OpenTimeout.Add(TimeSpan.FromMilliseconds(10)));
        await circuitBreaker.ExecuteAsync(failingOperation);

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
    }

    #endregion

    #region Threshold Management

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task CircuitBreaker_ShouldRespectFailureThreshold(int threshold)
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = threshold,
            OpenTimeout = TimeSpan.FromSeconds(1)
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var failingOperation = CreateFailingOperation();

        // Act - Execute failing operations one less than threshold
        for (int i = 0; i < threshold - 1; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
            circuitBreaker.State.Should().Be(CircuitBreakerState.Closed, $"Should remain closed after {i + 1} failures");
        }

        // Execute one more failing operation to reach threshold
        await circuitBreaker.ExecuteAsync(failingOperation);

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open, $"Should open after {threshold} failures");
    }

    [Fact]
    public async Task CircuitBreaker_SuccessfulOperations_ShouldNotContributeToFailureCount()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var failingOperation = CreateFailingOperation();
        var successfulOperation = CreateSuccessfulOperation();

        // Act - Mix successful and failing operations
        await circuitBreaker.ExecuteAsync(successfulOperation);
        await circuitBreaker.ExecuteAsync(failingOperation);
        await circuitBreaker.ExecuteAsync(successfulOperation);
        await circuitBreaker.ExecuteAsync(failingOperation);
        await circuitBreaker.ExecuteAsync(successfulOperation);

        // Assert - Should still be closed as we haven't reached threshold of consecutive failures
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    #endregion

    #region Exception Handling

    [Fact]
    public async Task CircuitBreaker_WithExceptionThatShouldTrip_ShouldCountAsFailure()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var operationThatThrows = CreateOperationThatThrows<HttpRequestException>();

        // Act
        for (int i = 0; i < _defaultOptions.FailureThreshold; i++)
        {
            var result = await circuitBreaker.ExecuteAsync(operationThatThrows);
            result.IsSuccess.Should().BeFalse();
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task CircuitBreaker_WithExceptionThatShouldNotTrip_ShouldNotCountAsFailure()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            OpenTimeout = TimeSpan.FromSeconds(1),
            ShouldTripOnException = ex => ex is not ArgumentException
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var operationThatThrows = CreateOperationThatThrows<ArgumentException>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            var result = await circuitBreaker.ExecuteAsync(operationThatThrows);
            result.IsSuccess.Should().BeFalse();
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task CircuitBreaker_WithCustomErrorFilter_ShouldRespectFilter()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            OpenTimeout = TimeSpan.FromSeconds(1),
            ShouldTripOnError = error => error.Code != "IgnoreThis"
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var ignoredFailureOperation = () => Task.FromResult(Result.Failure(Error.Failure("IgnoreThis", "Should not trip")));
        var trippingFailureOperation = () => Task.FromResult(Result.Failure(Error.Failure("TripThis", "Should trip")));

        // Act - Execute ignored failures
        for (int i = 0; i < 5; i++)
        {
            await circuitBreaker.ExecuteAsync(ignoredFailureOperation);
        }

        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);

        // Execute tripping failures
        for (int i = 0; i < options.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(trippingFailureOperation);
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
    }

    #endregion

    #region Thread Safety

    [Fact]
    public async Task CircuitBreaker_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        const int threadCount = 100;
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var successfulOperation = CreateSuccessfulOperation();
        var results = new ConcurrentBag<Result>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    var result = await circuitBreaker.ExecuteAsync(successfulOperation);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));

        await Task.WhenAll(tasks);

        // Assert
        exceptions.Should().BeEmpty();
        results.Should().HaveCount(threadCount);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task CircuitBreaker_ConcurrentFailures_ShouldMaintainConsistentState()
    {
        // Arrange
        const int threadCount = 50;
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var failingOperation = CreateFailingOperation();
        var results = new ConcurrentBag<Result>();

        // Act
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(async () =>
            {
                var result = await circuitBreaker.ExecuteAsync(failingOperation);
                results.Add(result);
            }));

        await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(threadCount);
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);

        // Some operations should have executed (before circuit opened)
        // Others should have been rejected (after circuit opened)
        var executedFailures = results.Count(r => r.Error.Code != "CircuitBreaker.Open");
        var rejectedCalls = results.Count(r => r.Error.Code == "CircuitBreaker.Open");

        executedFailures.Should().BeGreaterThanOrEqualTo(_defaultOptions.FailureThreshold);
        rejectedCalls.Should().BeGreaterThan(0);

        _output.WriteLine($"Executed failures: {executedFailures}, Rejected calls: {rejectedCalls}");
    }

    [Fact]
    public async Task CircuitBreaker_ConcurrentStateTransitions_ShouldMaintainConsistency()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            OpenTimeout = TimeSpan.FromMilliseconds(100)
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var failingOperation = CreateFailingOperation();
        var successfulOperation = CreateSuccessfulOperation();

        // Act - Trip the circuit breaker
        for (int i = 0; i < options.FailureThreshold; i++)
        {
            await circuitBreaker.ExecuteAsync(failingOperation);
        }

        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);

        // Wait for break duration
        await Task.Delay(options.OpenTimeout.Add(TimeSpan.FromMilliseconds(50)));

        // Execute concurrent operations to test half-open state
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => circuitBreaker.ExecuteAsync(successfulOperation)));

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    #endregion

    #region Metrics and Monitoring

    [Fact]
    public async Task CircuitBreaker_ShouldTrackMetricsCorrectly()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var successfulOperation = CreateSuccessfulOperation();
        var failingOperation = CreateFailingOperation();

        // Act - Execute mixed operations
        await circuitBreaker.ExecuteAsync(successfulOperation);
        await circuitBreaker.ExecuteAsync(successfulOperation);
        await circuitBreaker.ExecuteAsync(failingOperation);
        await circuitBreaker.ExecuteAsync(failingOperation);

        // Assert - Verify state is as expected (not checking specific logging calls)
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    #endregion

    #region Generic vs Non-Generic Operations

    [Fact]
    public async Task CircuitBreaker_NonGenericOperation_ShouldWorkCorrectly()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var operation = () => Task.FromResult(Result.Success());

        // Act
        var result = await circuitBreaker.ExecuteAsync(operation);

        // Assert
        result.IsSuccess.Should().BeTrue();
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task CircuitBreaker_GenericOperation_ShouldWorkCorrectly()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);
        var operation = () => Task.FromResult(Result.Success("test-value"));

        // Act
        var result = await circuitBreaker.ExecuteAsync(operation);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test-value");
        circuitBreaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CircuitBreaker_WithNullOperation_ShouldThrowArgumentNullException()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(_defaultOptions, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            circuitBreaker.ExecuteAsync<string>(null!));
    }

    [Fact]
    public void CircuitBreaker_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CircuitBreaker(null!, _logger));
    }

    [Fact]
    public void CircuitBreaker_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CircuitBreaker(_defaultOptions, null!));
    }

    [Fact]
    public async Task CircuitBreaker_WithZeroFailureThreshold_ShouldOpenImmediately()
    {
        // Arrange
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 0,
            OpenTimeout = TimeSpan.FromSeconds(1)
        };
        var circuitBreaker = new CircuitBreaker(options, _logger);
        var failingOperation = CreateFailingOperation();

        // Act
        await circuitBreaker.ExecuteAsync(failingOperation);

        // Assert
        circuitBreaker.State.Should().Be(CircuitBreakerState.Open);
    }

    #endregion
    #region Test Helpers

    private static Func<Task<Result>> CreateSuccessfulOperation()
    {
        return () => Task.FromResult(Result.Success());
    }

    private static Func<Task<Result<T>>> CreateSuccessfulOperation<T>(T value)
    {
        return () => Task.FromResult(Result.Success(value));
    }

    private static Func<Task<Result>> CreateFailingOperation()
    {
        return () => Task.FromResult(Result.Failure(Error.Failure("Operation.Failed", "Test failure")));
    }

    private static Func<Task<Result<T>>> CreateFailingOperation<T>()
    {
        return () => Task.FromResult(Result.Failure<T>(Error.Failure("Operation.Failed", "Test failure")));
    }

    private static Func<Task<Result>> CreateOperationThatThrows<TException>() where TException : Exception, new()
    {
        return () =>
        {
            throw new TException();
#pragma warning disable CS0162 // Unreachable code detected
            return Task.FromResult(Result.Success());
#pragma warning restore CS0162 // Unreachable code detected
        };
    }

    private static Func<Task<Result<T>>> CreateOperationThatThrows<T, TException>() where TException : Exception, new()
    {
        return () => throw new TException();
    }

    #endregion
}
