// -----------------------------------------------------------------------
// <copyright file="InMemoryTestStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Testing.TestHelpers;
using AwesomeAssertions;

namespace Compendium.Testing.Tests.TestHelpers;

/// <summary>
/// Unit tests for the <see cref="InMemoryTestStore"/> helper.
/// </summary>
public class InMemoryTestStoreTests
{
    [Fact]
    public async Task ExistsAsync_WhenKeyMissing_ReturnsFalse()
    {
        // Arrange
        var store = new InMemoryTestStore();

        // Act
        var exists = await store.ExistsAsync("missing-key");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyPresent_ReturnsTrue()
    {
        // Arrange
        var store = new InMemoryTestStore();
        await store.SetAsync("key-1", "value", TimeSpan.FromMinutes(1));

        // Act
        var exists = await store.ExistsAsync("key-1");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithCancellationToken_ReturnsFalseForMissingKey()
    {
        // Arrange
        var store = new InMemoryTestStore();
        using var cts = new CancellationTokenSource();

        // Act
        var exists = await store.ExistsAsync("missing-key", cts.Token);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WhenKeyMissing_ReturnsDefault()
    {
        // Arrange
        var store = new InMemoryTestStore();

        // Act
        var value = await store.GetAsync<string>("missing-key");

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenKeyMissingForValueType_ReturnsDefault()
    {
        // Arrange
        var store = new InMemoryTestStore();

        // Act
        var value = await store.GetAsync<int>("missing-key");

        // Assert
        value.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_WhenKeyPresentWithMatchingType_ReturnsStoredValue()
    {
        // Arrange
        var store = new InMemoryTestStore();
        await store.SetAsync("key-1", "stored-value", TimeSpan.FromMinutes(1));

        // Act
        var value = await store.GetAsync<string>("key-1");

        // Assert
        value.Should().Be("stored-value");
    }

    [Fact]
    public async Task GetAsync_WhenKeyPresentButTypeMismatch_ReturnsDefault()
    {
        // Arrange
        var store = new InMemoryTestStore();
        await store.SetAsync("key-1", "stored-string", TimeSpan.FromMinutes(1));

        // Act
        var value = await store.GetAsync<int>("key-1");

        // Assert
        value.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_WhenStoredValueIsNull_ReturnsDefault()
    {
        // Arrange
        var store = new InMemoryTestStore();
        await store.SetAsync<string?>("key-1", null, TimeSpan.FromMinutes(1));

        // Act
        var value = await store.GetAsync<string>("key-1");

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WithCancellationToken_ReturnsValue()
    {
        // Arrange
        var store = new InMemoryTestStore();
        await store.SetAsync("key-1", 42, TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource();

        // Act
        var value = await store.GetAsync<int>("key-1", cts.Token);

        // Assert
        value.Should().Be(42);
    }

    [Fact]
    public async Task SetAsync_WhenCalledTwiceWithSameKey_OverwritesValue()
    {
        // Arrange
        var store = new InMemoryTestStore();

        // Act
        await store.SetAsync("key-1", "first", TimeSpan.FromMinutes(1));
        await store.SetAsync("key-1", "second", TimeSpan.FromMinutes(1));

        // Assert
        var value = await store.GetAsync<string>("key-1");
        value.Should().Be("second");
    }

    [Fact]
    public async Task SetAsync_WithComplexType_RoundTripsValue()
    {
        // Arrange
        var store = new InMemoryTestStore();
        var payload = new SamplePayload("alice", 42);

        // Act
        await store.SetAsync("payload", payload, TimeSpan.FromMinutes(1));
        var roundTrip = await store.GetAsync<SamplePayload>("payload");

        // Assert
        roundTrip.Should().NotBeNull();
        roundTrip!.Name.Should().Be("alice");
        roundTrip.Count.Should().Be(42);
    }

    [Fact]
    public async Task SetAsync_WithCancellationToken_StoresValue()
    {
        // Arrange
        var store = new InMemoryTestStore();
        using var cts = new CancellationTokenSource();

        // Act
        await store.SetAsync("key-1", "value", TimeSpan.FromMinutes(1), cts.Token);

        // Assert
        (await store.ExistsAsync("key-1")).Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_WithZeroExpiration_StillStoresValue()
    {
        // Arrange
        var store = new InMemoryTestStore();

        // Act
        await store.SetAsync("key-1", "value", TimeSpan.Zero);

        // Assert — TTL is not enforced by this in-memory helper.
        (await store.ExistsAsync("key-1")).Should().BeTrue();
    }

    [Fact]
    public async Task Clear_WhenStorePopulated_RemovesAllEntries()
    {
        // Arrange
        var store = new InMemoryTestStore();
        await store.SetAsync("key-1", "a", TimeSpan.FromMinutes(1));
        await store.SetAsync("key-2", "b", TimeSpan.FromMinutes(1));

        // Act
        store.Clear();

        // Assert
        (await store.ExistsAsync("key-1")).Should().BeFalse();
        (await store.ExistsAsync("key-2")).Should().BeFalse();
    }

    [Fact]
    public void Clear_WhenStoreEmpty_DoesNotThrow()
    {
        // Arrange
        var store = new InMemoryTestStore();

        // Act
        var act = () => store.Clear();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task IdempotencyScenario_SecondSetWithSameKey_DoesNotDuplicate()
    {
        // Arrange — simulates an idempotency-key store
        var store = new InMemoryTestStore();
        var key = "idem-key-1";

        // Act
        var firstExists = await store.ExistsAsync(key);
        await store.SetAsync(key, "result-payload", TimeSpan.FromMinutes(5));
        var secondExists = await store.ExistsAsync(key);
        await store.SetAsync(key, "result-payload", TimeSpan.FromMinutes(5));
        var stored = await store.GetAsync<string>(key);

        // Assert
        firstExists.Should().BeFalse();
        secondExists.Should().BeTrue();
        stored.Should().Be("result-payload");
    }

    private sealed record SamplePayload(string Name, int Count);
}
