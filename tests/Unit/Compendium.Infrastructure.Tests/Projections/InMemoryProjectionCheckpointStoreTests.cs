// -----------------------------------------------------------------------
// <copyright file="InMemoryProjectionCheckpointStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.Projections;
using AwesomeAssertions;

namespace Compendium.Infrastructure.Tests.Projections;

public sealed class InMemoryProjectionCheckpointStoreTests
{
    private readonly InMemoryProjectionCheckpointStore _sut = new();

    [Fact]
    public async Task GetCheckpoint_WhenMissing_ReturnsZero()
    {
        // Act
        var result = await _sut.GetCheckpointAsync("projection-1", "agg-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task SaveCheckpoint_ThenGet_ReturnsSavedPosition()
    {
        // Act
        await _sut.SaveCheckpointAsync("projection-1", "agg-1", 42);
        var result = await _sut.GetCheckpointAsync("projection-1", "agg-1");

        // Assert
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task SaveCheckpoint_OverwritesExisting()
    {
        // Arrange
        await _sut.SaveCheckpointAsync("projection-1", "agg-1", 10);

        // Act
        await _sut.SaveCheckpointAsync("projection-1", "agg-1", 99);
        var result = await _sut.GetCheckpointAsync("projection-1", "agg-1");

        // Assert
        result.Value.Should().Be(99);
    }

    [Fact]
    public async Task Checkpoint_KeyedByProjectionAndAggregate()
    {
        // Arrange
        await _sut.SaveCheckpointAsync("projection-1", "agg-1", 10);
        await _sut.SaveCheckpointAsync("projection-1", "agg-2", 20);
        await _sut.SaveCheckpointAsync("projection-2", "agg-1", 30);

        // Act
        var p1a1 = await _sut.GetCheckpointAsync("projection-1", "agg-1");
        var p1a2 = await _sut.GetCheckpointAsync("projection-1", "agg-2");
        var p2a1 = await _sut.GetCheckpointAsync("projection-2", "agg-1");

        // Assert
        p1a1.Value.Should().Be(10);
        p1a2.Value.Should().Be(20);
        p2a1.Value.Should().Be(30);
    }

    [Fact]
    public async Task DeleteCheckpoint_RemovesEntry()
    {
        // Arrange
        await _sut.SaveCheckpointAsync("projection-1", "agg-1", 42);

        // Act
        await _sut.DeleteCheckpointAsync("projection-1", "agg-1");
        var result = await _sut.GetCheckpointAsync("projection-1", "agg-1");

        // Assert
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task DeleteCheckpoint_WhenMissing_IsNoOp()
    {
        // Act
        var act = () => _sut.DeleteCheckpointAsync("projection-1", "agg-missing");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCheckpoint_NullOrWhitespaceProjectionId_Throws(string? id)
    {
        var act = () => _sut.GetCheckpointAsync(id!, "agg-1");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveCheckpoint_NullOrWhitespaceAggregateId_Throws(string? id)
    {
        var act = () => _sut.SaveCheckpointAsync("projection-1", id!, 42);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
