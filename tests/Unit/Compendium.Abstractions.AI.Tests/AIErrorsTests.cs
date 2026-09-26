using Compendium.Core.Results;
using AwesomeAssertions;

namespace Compendium.Abstractions.AI.Tests;

public class AIErrorsTests
{
    [Fact]
    public void ModelNotFound_ShouldReturnNotFoundError()
    {
        // Act
        var error = AIErrors.ModelNotFound("gpt-5");

        // Assert
        error.Code.Should().Be("AI.ModelNotFound");
        error.Message.Should().Contain("gpt-5");
        error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void ProviderUnavailable_ShouldReturnFailureError()
    {
        // Act
        var error = AIErrors.ProviderUnavailable("openrouter");

        // Assert
        error.Code.Should().Be("AI.ProviderUnavailable");
        error.Message.Should().Contain("openrouter");
        error.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void RateLimitExceeded_WithoutRetryAfter_ShouldReturnGenericMessage()
    {
        // Act
        var error = AIErrors.RateLimitExceeded();

        // Assert
        error.Code.Should().Be("AI.RateLimitExceeded");
        error.Message.Should().Contain("Rate limit exceeded");
    }

    [Fact]
    public void RateLimitExceeded_WithRetryAfter_ShouldIncludeRetryTime()
    {
        // Arrange
        var retryAfter = TimeSpan.FromSeconds(30);

        // Act
        var error = AIErrors.RateLimitExceeded(retryAfter);

        // Assert
        error.Code.Should().Be("AI.RateLimitExceeded");
        error.Message.Should().Contain("30");
    }

    [Fact]
    public void TokenLimitExceeded_ShouldReturnValidationError()
    {
        // Act
        var error = AIErrors.TokenLimitExceeded(10000, 4096);

        // Assert
        error.Code.Should().Be("AI.TokenLimitExceeded");
        error.Message.Should().Contain("10000");
        error.Message.Should().Contain("4096");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ContentFiltered_WithReason_ShouldIncludeReason()
    {
        // Act
        var error = AIErrors.ContentFiltered("Harmful content detected");

        // Assert
        error.Code.Should().Be("AI.ContentFiltered");
        error.Message.Should().Be("Harmful content detected");
    }

    [Fact]
    public void ContentFiltered_WithoutReason_ShouldUseDefaultMessage()
    {
        // Act
        var error = AIErrors.ContentFiltered();

        // Assert
        error.Message.Should().Contain("safety reasons");
    }

    [Fact]
    public void InvalidApiKey_ShouldReturnFailureError()
    {
        // Act
        var error = AIErrors.InvalidApiKey();

        // Assert
        error.Code.Should().Be("AI.InvalidApiKey");
        error.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void InsufficientCredits_ShouldReturnFailureError()
    {
        // Act
        var error = AIErrors.InsufficientCredits();

        // Assert
        error.Code.Should().Be("AI.InsufficientCredits");
        error.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void Timeout_ShouldIncludeElapsedTime()
    {
        // Arrange
        var elapsed = TimeSpan.FromSeconds(120);

        // Act
        var error = AIErrors.Timeout(elapsed);

        // Assert
        error.Code.Should().Be("AI.Timeout");
        error.Message.Should().Contain("120");
    }

    [Fact]
    public void PromptNotFound_ShouldReturnNotFoundError()
    {
        // Act
        var error = AIErrors.PromptNotFound("welcome_message");

        // Assert
        error.Code.Should().Be("AI.PromptNotFound");
        error.Message.Should().Contain("welcome_message");
        error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void MissingVariables_ShouldListVariables()
    {
        // Arrange
        var variables = new[] { "name", "email", "company" };

        // Act
        var error = AIErrors.MissingVariables(variables);

        // Assert
        error.Code.Should().Be("AI.MissingVariables");
        error.Message.Should().Contain("name");
        error.Message.Should().Contain("email");
        error.Message.Should().Contain("company");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ContextBuildFailed_ShouldIncludeReason()
    {
        // Act
        var error = AIErrors.ContextBuildFailed("User preferences not found");

        // Assert
        error.Code.Should().Be("AI.ContextBuildFailed");
        error.Message.Should().Contain("User preferences not found");
    }

    [Fact]
    public void StreamInterrupted_WithReason_ShouldIncludeReason()
    {
        // Act
        var error = AIErrors.StreamInterrupted("Connection reset");

        // Assert
        error.Code.Should().Be("AI.StreamInterrupted");
        error.Message.Should().Be("Connection reset");
    }

    [Fact]
    public void ProviderError_WithErrorCode_ShouldIncludeCode()
    {
        // Act
        var error = AIErrors.ProviderError("Internal server error", "500");

        // Assert
        error.Code.Should().Be("AI.ProviderError");
        error.Message.Should().Contain("[500]");
        error.Message.Should().Contain("Internal server error");
    }

    [Fact]
    public void InvalidRequest_ShouldReturnValidationError()
    {
        // Act
        var error = AIErrors.InvalidRequest("Messages array cannot be empty");

        // Assert
        error.Code.Should().Be("AI.InvalidRequest");
        error.Message.Should().Be("Messages array cannot be empty");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void RerankFailed_ShouldReturnFailureErrorWithReason()
    {
        // Act
        var error = AIErrors.RerankFailed("upstream 503");

        // Assert
        error.Code.Should().Be("AI.RerankFailed");
        error.Message.Should().Contain("upstream 503");
        error.Type.Should().Be(ErrorType.Failure);
    }

    [Fact]
    public void InvalidQuery_ShouldReturnValidationError()
    {
        // Act
        var error = AIErrors.InvalidQuery("Query must not be empty");

        // Assert
        error.Code.Should().Be("AI.InvalidQuery");
        error.Message.Should().Be("Query must not be empty");
        error.Type.Should().Be(ErrorType.Validation);
    }
}
