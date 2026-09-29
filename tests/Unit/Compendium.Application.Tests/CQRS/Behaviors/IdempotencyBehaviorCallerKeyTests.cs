// -----------------------------------------------------------------------
// <copyright file="IdempotencyBehaviorCallerKeyTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Compendium.Application.CQRS.Behaviors;
using Compendium.Application.Idempotency;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Application.Tests.CQRS.Behaviors;

/// <summary>
/// The caller-keyed mode of <see cref="IdempotencyBehavior{TRequest, TResponse}"/>: a command
/// implementing <see cref="IIdempotentRequest"/> runs once per key, and every replay gets the
/// recorded response.
/// </summary>
public sealed class IdempotencyBehaviorCallerKeyTests
{
    public sealed record DeployCommand(string Tag, string? IdempotencyKey) : ICommand<Result<string>>, IIdempotentRequest;

    public sealed record PlainCommand(string Tag) : ICommand<Result<string>>;

    private readonly AtomicStore _store = new();

    private IdempotencyBehavior<TCommand, Result<string>> Behavior<TCommand>(IIdempotencyService? service = null)
        where TCommand : class
        => new(service ?? new IdempotencyService(_store), NullLogger<IdempotencyBehavior<TCommand, Result<string>>>.Instance)
        {
            ReplayWait = TimeSpan.FromMilliseconds(200),
        };

    [Fact]
    public async Task SameKeyTwice_RunsTheHandlerOnce_AndReplaysTheSameResponse()
    {
        var behavior = Behavior<DeployCommand>();
        var runs = 0;
        Task<Result<string>> Handler() => Task.FromResult(Result.Success($"deployment-{Interlocked.Increment(ref runs)}"));

        var first = await behavior.HandleAsync(new DeployCommand("v1", "key-1"), Handler, CancellationToken.None);
        var second = await behavior.HandleAsync(new DeployCommand("v1", "key-1"), Handler, CancellationToken.None);

        runs.Should().Be(1);
        second.Value.Should().Be(first.Value);
    }

    [Fact]
    public async Task AFailureIsRecordedToo_TheReplayReturnsIt_WithoutRunningAgain()
    {
        var behavior = Behavior<DeployCommand>();
        var runs = 0;
        Task<Result<string>> Handler()
        {
            Interlocked.Increment(ref runs);
            return Task.FromResult(Result.Failure<string>(Error.Validation("Deploy.BadTag", "no such tag")));
        }

        await behavior.HandleAsync(new DeployCommand("v9", "key-2"), Handler, CancellationToken.None);
        var replay = await behavior.HandleAsync(new DeployCommand("v9", "key-2"), Handler, CancellationToken.None);

        runs.Should().Be(1);
        replay.IsFailure.Should().BeTrue();
        replay.Error.Code.Should().Be("Deploy.BadTag");
    }

    [Fact]
    public async Task IdenticalContent_UnderDifferentKeys_RunsTwice_BecauseTheKeyIsTheCallers()
    {
        var behavior = Behavior<DeployCommand>();
        var runs = 0;
        Task<Result<string>> Handler() => Task.FromResult(Result.Success($"deployment-{Interlocked.Increment(ref runs)}"));

        await behavior.HandleAsync(new DeployCommand("v1", "key-a"), Handler, CancellationToken.None);
        await behavior.HandleAsync(new DeployCommand("v1", "key-b"), Handler, CancellationToken.None);

        runs.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoKey_MeansNoDeduplication_AndTouchesNoStore(string? key)
    {
        var behavior = Behavior<DeployCommand>();
        var runs = 0;
        Task<Result<string>> Handler() => Task.FromResult(Result.Success($"deployment-{Interlocked.Increment(ref runs)}"));

        await behavior.HandleAsync(new DeployCommand("v1", key), Handler, CancellationToken.None);
        await behavior.HandleAsync(new DeployCommand("v1", key), Handler, CancellationToken.None);

        runs.Should().Be(2);
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentCallersWithOneKey_RunTheHandlerExactlyOnce_AndAllGetItsResponse()
    {
        var behavior = Behavior<DeployCommand>();
        var runs = 0;
        var release = new TaskCompletionSource();
        async Task<Result<string>> Handler()
        {
            var n = Interlocked.Increment(ref runs);
            await release.Task;
            return Result.Success($"deployment-{n}");
        }

        var callers = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => behavior.HandleAsync(new DeployCommand("v1", "key-3"), Handler, CancellationToken.None)))
            .ToArray();
        await Task.Delay(50);
        release.SetResult();
        var responses = await Task.WhenAll(callers);

        runs.Should().Be(1);
        responses.Select(r => r.Value).Distinct().Should().ContainSingle().Which.Should().Be("deployment-1");
    }

    [Fact]
    public async Task AReplayWhileTheWinnerIsStillRunning_GetsAnInProgressConflict_NotASecondRun()
    {
        var behavior = Behavior<DeployCommand>();
        var runs = 0;
        var release = new TaskCompletionSource();
        async Task<Result<string>> Handler()
        {
            Interlocked.Increment(ref runs);
            await release.Task;
            return Result.Success("done");
        }

        var winner = behavior.HandleAsync(new DeployCommand("v1", "key-4"), Handler, CancellationToken.None);
        Result<string> replay;
        try
        {
            // Bounded: if the replay ever ran the handler itself, it would wait on `release`
            // forever. The bound turns that regression into a failure instead of a hung run.
            replay = await behavior.HandleAsync(new DeployCommand("v1", "key-4"), Handler, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.TrySetResult();
        }

        await winner;

        runs.Should().Be(1);
        replay.IsFailure.Should().BeTrue();
        replay.Error.Code.Should().Be("Idempotency.InProgress");
        replay.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task AServiceWithoutReservation_FallsBackToCheckThenAct_AndStillReplaysSequentialCalls()
    {
        var behavior = Behavior<DeployCommand>(new NonReservingService(new IdempotencyService(_store)));
        var runs = 0;
        Task<Result<string>> Handler() => Task.FromResult(Result.Success($"deployment-{Interlocked.Increment(ref runs)}"));

        var first = await behavior.HandleAsync(new DeployCommand("v1", "key-5"), Handler, CancellationToken.None);
        var second = await behavior.HandleAsync(new DeployCommand("v1", "key-5"), Handler, CancellationToken.None);

        runs.Should().Be(1);
        second.Value.Should().Be(first.Value);
    }

    [Fact]
    public async Task ACommandWithoutTheInterface_KeepsTheContentHashKey()
    {
        var behavior = Behavior<PlainCommand>();
        var runs = 0;
        Task<Result<string>> Handler() => Task.FromResult(Result.Success($"deployment-{Interlocked.Increment(ref runs)}"));

        await behavior.HandleAsync(new PlainCommand("v1"), Handler, CancellationToken.None);
        await behavior.HandleAsync(new PlainCommand("v1"), Handler, CancellationToken.None);

        runs.Should().Be(1);
        _store.Keys.Should().ContainSingle().Which.Should().StartWith("idempotency:").And.NotContain(":request:");
    }

    /// <summary>A reservation store with real atomicity, standing in for the infrastructure one.</summary>
    private sealed class AtomicStore : IIdempotencyReservationStore
    {
        private readonly ConcurrentDictionary<string, object?> _entries = new();

        public int Count => _entries.Count;

        public IEnumerable<string> Keys => _entries.Keys;

        public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(_entries.ContainsKey(key)));

        public Task<Result<TResult?>> GetAsync<TResult>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success<TResult?>(_entries.TryGetValue(key, out var v) && v is TResult t ? t : default));

        public Task<Result> SetAsync<TValue>(string key, TValue value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            _entries[key] = value;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<bool>> TryReserveAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(_entries.TryAdd(key, "reserved")));
    }

    /// <summary>An <see cref="IIdempotencyService"/> that does not offer reservations.</summary>
    private sealed class NonReservingService(IIdempotencyService inner) : IIdempotencyService
    {
        public Task<bool> IsProcessedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
            => inner.IsProcessedAsync(idempotencyKey, cancellationToken);

        public Task<TResult?> GetResultAsync<TResult>(string idempotencyKey, CancellationToken cancellationToken = default)
            => inner.GetResultAsync<TResult>(idempotencyKey, cancellationToken);

        public Task SetResultAsync<TResult>(string idempotencyKey, TResult result, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => inner.SetResultAsync(idempotencyKey, result, expiration, cancellationToken);

        public Task MarkAsProcessedAsync(string idempotencyKey, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            => inner.MarkAsProcessedAsync(idempotencyKey, expiration, cancellationToken);
    }
}
