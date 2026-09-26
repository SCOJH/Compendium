// -----------------------------------------------------------------------
// <copyright file="InMemoryProjectionStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.Projections;
using AwesomeAssertions;

namespace Compendium.Infrastructure.Tests.Projections;

public sealed class InMemoryProjectionStoreTests
{
    private readonly InMemoryProjectionStore _sut = new();

    [Fact]
    public async Task GetCheckpoint_WhenMissing_ReturnsNull()
    {
        var result = await _sut.GetCheckpointAsync("p-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveCheckpoint_ThenGet_ReturnsValue()
    {
        await _sut.SaveCheckpointAsync("p-1", 42);
        var result = await _sut.GetCheckpointAsync("p-1");
        result.Should().Be(42);
    }

    [Fact]
    public async Task SaveCheckpoint_Overwrites()
    {
        await _sut.SaveCheckpointAsync("p-1", 10);
        await _sut.SaveCheckpointAsync("p-1", 99);
        var result = await _sut.GetCheckpointAsync("p-1");
        result.Should().Be(99);
    }

    [Fact]
    public async Task GetProjectionState_WhenMissing_ReturnsNull()
    {
        var result = await _sut.GetProjectionStateAsync("p-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveProjectionState_ThenGet_ReturnsSameInstance()
    {
        var state = new ProjectionState
        {
            ProjectionName = "p-1",
            Version = 2,
            LastProcessedPosition = 17,
            LastProcessedAt = DateTime.UtcNow,
        };

        await _sut.SaveProjectionStateAsync(state);
        var result = await _sut.GetProjectionStateAsync("p-1");

        result.Should().NotBeNull();
        result!.ProjectionName.Should().Be("p-1");
        result.Version.Should().Be(2);
        result.LastProcessedPosition.Should().Be(17);
    }

    [Fact]
    public async Task SaveSnapshot_ThenLoad_RoundtripsThroughJson()
    {
        var projection = new TestProjection { ProjectionName = "p-snap", Version = 1, Counter = 42 };
        await _sut.SaveSnapshotAsync(projection);

        var loaded = await _sut.LoadSnapshotAsync<TestProjection>("p-snap");

        loaded.Should().NotBeNull();
        loaded!.ProjectionName.Should().Be("p-snap");
        loaded.Counter.Should().Be(42);
    }

    [Fact]
    public async Task LoadSnapshot_WhenMissing_ReturnsDefault()
    {
        var loaded = await _sut.LoadSnapshotAsync<TestProjection>("missing");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProjectionData_RemovesAllArtifacts()
    {
        await _sut.SaveCheckpointAsync("p-1", 10);
        await _sut.SaveSnapshotAsync(new TestProjection { ProjectionName = "p-1", Counter = 5 });
        await _sut.SaveProjectionStateAsync(new ProjectionState { ProjectionName = "p-1" });

        await _sut.DeleteProjectionDataAsync("p-1");

        (await _sut.GetCheckpointAsync("p-1")).Should().BeNull();
        (await _sut.LoadSnapshotAsync<TestProjection>("p-1")).Should().BeNull();
        (await _sut.GetProjectionStateAsync("p-1")).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProjectionName_NullOrWhitespace_Throws(string? name)
    {
        var act1 = () => _sut.GetCheckpointAsync(name!);
        var act2 = () => _sut.SaveCheckpointAsync(name!, 0);
        var act3 = () => _sut.LoadSnapshotAsync<TestProjection>(name!);
        var act4 = () => _sut.DeleteProjectionDataAsync(name!);

        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
        await act3.Should().ThrowAsync<ArgumentException>();
        await act4.Should().ThrowAsync<ArgumentException>();
    }

    public sealed class TestProjection : Compendium.Infrastructure.Projections.IProjection
    {
        public string ProjectionName { get; set; } = "test";

        public int Version { get; set; } = 1;

        public int Counter { get; set; }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Counter = 0;
            return Task.CompletedTask;
        }
    }
}
