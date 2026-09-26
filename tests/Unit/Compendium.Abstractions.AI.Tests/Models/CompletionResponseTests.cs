using Compendium.Abstractions.AI.Models;
using AwesomeAssertions;

namespace Compendium.Abstractions.AI.Tests.Models;

public class CompletionResponseTests
{
    [Fact]
    public void CompletionResponse_WithRequiredProperties_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var response = new CompletionResponse
        {
            Id = "completion-123",
            Model = "anthropic/claude-3.5-sonnet",
            Content = "Hello! How can I help you?",
            FinishReason = FinishReason.Stop,
            Usage = new UsageStats
            {
                PromptTokens = 10,
                CompletionTokens = 8
            }
        };

        // Assert
        response.Id.Should().Be("completion-123");
        response.Model.Should().Be("anthropic/claude-3.5-sonnet");
        response.Content.Should().Be("Hello! How can I help you?");
        response.FinishReason.Should().Be(FinishReason.Stop);
        response.Usage.TotalTokens.Should().Be(18);
    }

    [Fact]
    public void CompletionResponse_CreatedAt_ShouldDefaultToUtcNow()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var response = new CompletionResponse
        {
            Id = "test",
            Model = "test",
            Content = "test",
            FinishReason = FinishReason.Stop,
            Usage = new UsageStats { PromptTokens = 1, CompletionTokens = 1 }
        };

        var after = DateTime.UtcNow;

        // Assert
        response.CreatedAt.Should().BeOnOrAfter(before);
        response.CreatedAt.Should().BeOnOrBefore(after);
    }
}

public class CompletionChunkTests
{
    [Fact]
    public void CompletionChunk_NonFinal_ShouldNotHaveFinishReason()
    {
        // Arrange & Act
        var chunk = new CompletionChunk
        {
            Id = "chunk-1",
            ContentDelta = "Hello",
            Index = 0,
            IsFinal = false
        };

        // Assert
        chunk.IsFinal.Should().BeFalse();
        chunk.FinishReason.Should().BeNull();
        chunk.Usage.Should().BeNull();
    }

    [Fact]
    public void CompletionChunk_Final_ShouldHaveFinishReasonAndUsage()
    {
        // Arrange & Act
        var chunk = new CompletionChunk
        {
            Id = "chunk-final",
            ContentDelta = "",
            Index = 5,
            IsFinal = true,
            FinishReason = FinishReason.Stop,
            Usage = new UsageStats { PromptTokens = 10, CompletionTokens = 20 }
        };

        // Assert
        chunk.IsFinal.Should().BeTrue();
        chunk.FinishReason.Should().Be(FinishReason.Stop);
        chunk.Usage.Should().NotBeNull();
        chunk.Usage!.TotalTokens.Should().Be(30);
    }
}

public class UsageStatsTests
{
    [Theory]
    [InlineData(10, 5, 15)]
    [InlineData(100, 200, 300)]
    [InlineData(0, 0, 0)]
    [InlineData(1000, 500, 1500)]
    public void UsageStats_TotalTokens_ShouldSumPromptAndCompletion(
        int promptTokens,
        int completionTokens,
        int expectedTotal)
    {
        // Arrange & Act
        var usage = new UsageStats
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens
        };

        // Assert
        usage.TotalTokens.Should().Be(expectedTotal);
    }

    [Fact]
    public void UsageStats_WithEstimatedCost_ShouldIncludeCost()
    {
        // Arrange & Act
        var usage = new UsageStats
        {
            PromptTokens = 1000,
            CompletionTokens = 500,
            EstimatedCostUsd = 0.015m
        };

        // Assert
        usage.EstimatedCostUsd.Should().Be(0.015m);
    }
}

public class FinishReasonTests
{
    [Fact]
    public void FinishReason_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<FinishReason>().Should().Contain(new[]
        {
            FinishReason.Stop,
            FinishReason.Length,
            FinishReason.ContentFilter,
            FinishReason.ToolCall,
            FinishReason.InProgress,
            FinishReason.Other
        });
    }
}
