// -----------------------------------------------------------------------
// <copyright file="IdempotencyServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Application.Idempotency;

namespace Compendium.Application.Tests.Idempotency;

/// <summary>
/// Unit tests for the <see cref="IdempotencyService"/> class.
/// </summary>
public class IdempotencyServiceTests
{
    [Fact]
    public void Constructor_WhenStoreIsNull_ThrowsArgumentNullException()
    {
        // Arrange / Act
        var act = () => new IdempotencyService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }

    [Fact]
    public void Constructor_DefaultsExpirationTo24Hours_WhenNotProvided()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        var service = new IdempotencyService(store);

        // Act
        _ = service.SetResultAsync("k", 42, expiration: null, CancellationToken.None);

        // Assert
        store.Received(1).SetAsync(
            "k",
            42,
            TimeSpan.FromHours(24),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsProcessedAsync_WhenKeyInvalid_ThrowsArgumentException(string? key)
    {
        // Arrange
        var service = new IdempotencyService(Substitute.For<IIdempotencyStore>());

        // Act
        var act = async () => await service.IsProcessedAsync(key!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("idempotencyKey");
    }

    [Fact]
    public async Task IsProcessedAsync_WhenStoreReturnsTrue_ReturnsTrue()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync("k", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(true)));
        var service = new IdempotencyService(store);

        // Act
        var result = await service.IsProcessedAsync("k", CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsProcessedAsync_WhenStoreReturnsFalse_ReturnsFalse()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync("k", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(false)));
        var service = new IdempotencyService(store);

        // Act
        var result = await service.IsProcessedAsync("k", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsProcessedAsync_WhenStoreFails_GracefullyReturnsFalse()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync("k", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<bool>(Error.Failure("Store.Down", "down"))));
        var service = new IdempotencyService(store);

        // Act
        var result = await service.IsProcessedAsync("k", CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\t")]
    public async Task GetResultAsync_WhenKeyInvalid_ThrowsArgumentException(string? key)
    {
        // Arrange
        var service = new IdempotencyService(Substitute.For<IIdempotencyStore>());

        // Act
        var act = async () => await service.GetResultAsync<int>(key!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("idempotencyKey");
    }

    [Fact]
    public async Task GetResultAsync_WhenStoreReturnsValue_ReturnsValue()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.GetAsync<string>("k", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<string?>("v")));
        var service = new IdempotencyService(store);

        // Act
        var result = await service.GetResultAsync<string>("k", CancellationToken.None);

        // Assert
        result.Should().Be("v");
    }

    [Fact]
    public async Task GetResultAsync_WhenStoreFails_GracefullyReturnsDefault()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.GetAsync<string>("k", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<string?>(Error.Failure("Store.Down", "down"))));
        var service = new IdempotencyService(store);

        // Act
        var result = await service.GetResultAsync<string>("k", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SetResultAsync_WhenKeyInvalid_ThrowsArgumentException(string? key)
    {
        // Arrange
        var service = new IdempotencyService(Substitute.For<IIdempotencyStore>());

        // Act
        var act = async () => await service.SetResultAsync(key!, 1, expiration: null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("idempotencyKey");
    }

    [Fact]
    public async Task SetResultAsync_UsesProvidedExpirationOverride()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var service = new IdempotencyService(store);

        // Act
        await service.SetResultAsync("k", 1, TimeSpan.FromMinutes(5), CancellationToken.None);

        // Assert
        await store.Received(1).SetAsync(
            "k",
            1,
            TimeSpan.FromMinutes(5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetResultAsync_WhenStoreFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(Error.Failure("Store.Down", "broken"))));
        var service = new IdempotencyService(store);

        // Act
        var act = async () => await service.SetResultAsync("k", 1, expiration: null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*broken*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n")]
    public async Task MarkAsProcessedAsync_WhenKeyInvalid_ThrowsArgumentException(string? key)
    {
        // Arrange
        var service = new IdempotencyService(Substitute.For<IIdempotencyStore>());

        // Act
        var act = async () => await service.MarkAsProcessedAsync(key!, expiration: null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("idempotencyKey");
    }

    [Fact]
    public async Task MarkAsProcessedAsync_DelegatesToStoreSet()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.SetAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var service = new IdempotencyService(store);

        // Act
        await service.MarkAsProcessedAsync("k", TimeSpan.FromMinutes(1), CancellationToken.None);

        // Assert
        await store.Received(1).SetAsync("k", true, TimeSpan.FromMinutes(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAsProcessedAsync_WhenStoreFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var store = Substitute.For<IIdempotencyStore>();
        store.SetAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure(Error.Failure("Store.Down", "down"))));
        var service = new IdempotencyService(store);

        // Act
        var act = async () => await service.MarkAsProcessedAsync("k", expiration: null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*down*");
    }
    [Fact]
    public async Task TryReserve_WhenTheStoreCannotReserve_FailsExplicitly_InsteadOfPretending()
    {
        var sut = new IdempotencyService(Substitute.For<IIdempotencyStore>());

        var result = await sut.TryReserveAsync("key");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Idempotency.ReservationUnsupported");
    }

    [Fact]
    public async Task TryReserve_UsesTheResultLifetime_OnAKeyDistinctFromTheResult()
    {
        var store = Substitute.For<IIdempotencyReservationStore>();
        store.TryReserveAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        var sut = new IdempotencyService(store, TimeSpan.FromMinutes(42));

        var result = await sut.TryReserveAsync("key");

        result.Value.Should().BeTrue();
        await store.Received(1).TryReserveAsync("key:reservation", TimeSpan.FromMinutes(42), Arg.Any<CancellationToken>());
    }
}
