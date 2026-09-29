// -----------------------------------------------------------------------
// <copyright file="InMemoryIdempotencyStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.Idempotency;
using AwesomeAssertions;

namespace Compendium.Infrastructure.Tests.Idempotency;

public sealed class InMemoryIdempotencyStoreTests
{
    private readonly InMemoryIdempotencyStore _sut = new();

    [Fact]
    public async Task Exists_WhenKeyMissing_ReturnsFalse()
    {
        var result = await _sut.ExistsAsync("missing");
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task Set_ThenExists_ReturnsTrue()
    {
        await _sut.SetAsync("k1", "value", TimeSpan.FromMinutes(5));
        var result = await _sut.ExistsAsync("k1");
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Get_TypedRetrieval_ReturnsValue()
    {
        await _sut.SetAsync("order-1", new { OrderId = 42 }, TimeSpan.FromMinutes(5));

        var result = await _sut.GetAsync<object>("order-1");

        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_WhenKeyMissing_ReturnsDefault()
    {
        var result = await _sut.GetAsync<string>("missing");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Get_WhenTypeMismatch_ReturnsDefault()
    {
        await _sut.SetAsync("k1", 42, TimeSpan.FromMinutes(5));

        // Stored int, requested string.
        var result = await _sut.GetAsync<string>("k1");

        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Exists_AfterExpiration_ReturnsFalse()
    {
        await _sut.SetAsync("k1", "value", TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        var result = await _sut.ExistsAsync("k1");

        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task Get_AfterExpiration_ReturnsDefault()
    {
        await _sut.SetAsync("k1", "value", TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        var result = await _sut.GetAsync<string>("k1");

        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Set_OverwritesPriorValue()
    {
        await _sut.SetAsync("k1", "old", TimeSpan.FromMinutes(5));
        await _sut.SetAsync("k1", "new", TimeSpan.FromMinutes(5));

        var result = await _sut.GetAsync<string>("k1");

        result.Value.Should().Be("new");
    }

    [Fact]
    public async Task Set_ZeroOrNegativeExpiration_ReturnsFailure()
    {
        var zero = await _sut.SetAsync("k1", "v", TimeSpan.Zero);
        var negative = await _sut.SetAsync("k2", "v", TimeSpan.FromMinutes(-1));

        zero.IsFailure.Should().BeTrue();
        zero.Error.Code.Should().Be("Idempotency.InvalidExpiration");
        negative.IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NullOrWhitespaceKey_Throws(string? key)
    {
        var setAct = () => _sut.SetAsync(key!, "v", TimeSpan.FromMinutes(1));
        var getAct = () => _sut.GetAsync<string>(key!);
        var existsAct = () => _sut.ExistsAsync(key!);

        await setAct.Should().ThrowAsync<ArgumentException>();
        await getAct.Should().ThrowAsync<ArgumentException>();
        await existsAct.Should().ThrowAsync<ArgumentException>();
    }
    [Fact]
    public async Task TryReserve_FirstCallerWins_SecondLoses()
    {
        (await _sut.TryReserveAsync("r1", TimeSpan.FromMinutes(5))).Value.Should().BeTrue();
        (await _sut.TryReserveAsync("r1", TimeSpan.FromMinutes(5))).Value.Should().BeFalse();
    }

    [Fact]
    public async Task TryReserve_UnderContention_ExactlyOneCallerWins()
    {
        var gate = new TaskCompletionSource();
        var contenders = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                return (await _sut.TryReserveAsync("contended", TimeSpan.FromMinutes(5))).Value;
            }))
            .ToArray();

        gate.SetResult();
        var outcomes = await Task.WhenAll(contenders);

        outcomes.Count(won => won).Should().Be(1);
    }

    [Fact]
    public async Task TryReserve_AfterTheReservationExpired_CanBeWonAgain()
    {
        (await _sut.TryReserveAsync("r2", TimeSpan.FromMilliseconds(20))).Value.Should().BeTrue();
        await Task.Delay(60);

        (await _sut.TryReserveAsync("r2", TimeSpan.FromMinutes(5))).Value.Should().BeTrue();
    }

    [Fact]
    public async Task TryReserve_WithANonPositiveExpiration_Fails()
    {
        var result = await _sut.TryReserveAsync("r3", TimeSpan.Zero);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.InvalidExpiration");
    }
}
