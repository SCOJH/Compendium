using Compendium.Abstractions.AI.Models;
using AwesomeAssertions;

namespace Compendium.Abstractions.AI.Tests.Models;

public class CompletionRequestTests
{
    [Fact]
    public void CompletionRequest_WithRequiredProperties_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var request = new CompletionRequest
        {
            Model = "anthropic/claude-3.5-sonnet",
            Messages = new List<Message>
            {
                Message.User("Hello, world!")
            }
        };

        // Assert
        request.Model.Should().Be("anthropic/claude-3.5-sonnet");
        request.Messages.Should().HaveCount(1);
        request.Temperature.Should().Be(0.7f); // Default value
    }

    [Fact]
    public void CompletionRequest_WithAllProperties_ShouldCreateSuccessfully()
    {
        // Arrange & Act
        var request = new CompletionRequest
        {
            Model = "openai/gpt-4o",
            Messages = new List<Message>
            {
                Message.System("You are a helpful assistant."),
                Message.User("What is 2+2?")
            },
            SystemPrompt = "Be concise.",
            Temperature = 0.5f,
            MaxTokens = 1000,
            TopP = 0.9f,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.1f,
            StopSequences = new[] { "\n\n" },
            RequestId = "req-123",
            TenantId = "tenant-abc",
            UserId = "user-xyz"
        };

        // Assert
        request.Model.Should().Be("openai/gpt-4o");
        request.Messages.Should().HaveCount(2);
        request.SystemPrompt.Should().Be("Be concise.");
        request.Temperature.Should().Be(0.5f);
        request.MaxTokens.Should().Be(1000);
        request.TopP.Should().Be(0.9f);
        request.FrequencyPenalty.Should().Be(0.1f);
        request.PresencePenalty.Should().Be(0.1f);
        request.StopSequences.Should().Contain("\n\n");
        request.RequestId.Should().Be("req-123");
        request.TenantId.Should().Be("tenant-abc");
        request.UserId.Should().Be("user-xyz");
    }
}

public class MessageTests
{
    [Fact]
    public void Message_System_ShouldCreateSystemMessage()
    {
        // Act
        var message = Message.System("You are helpful.");

        // Assert
        message.Role.Should().Be(MessageRole.System);
        message.Content.Should().Be("You are helpful.");
    }

    [Fact]
    public void Message_User_ShouldCreateUserMessage()
    {
        // Act
        var message = Message.User("Hello!");

        // Assert
        message.Role.Should().Be(MessageRole.User);
        message.Content.Should().Be("Hello!");
    }

    [Fact]
    public void Message_Assistant_ShouldCreateAssistantMessage()
    {
        // Act
        var message = Message.Assistant("Hi there!");

        // Assert
        message.Role.Should().Be(MessageRole.Assistant);
        message.Content.Should().Be("Hi there!");
    }

    [Fact]
    public void Message_WithName_ShouldIncludeName()
    {
        // Act
        var message = new Message
        {
            Role = MessageRole.User,
            Content = "Hello",
            Name = "John"
        };

        // Assert
        message.Name.Should().Be("John");
    }
}
