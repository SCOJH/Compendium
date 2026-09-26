// -----------------------------------------------------------------------
// <copyright file="StandardAgentTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.AI;
using Compendium.Abstractions.AI.Agents;
using Compendium.Abstractions.AI.Agents.Models;
using Compendium.Abstractions.AI.Models;
using Compendium.Application.AI.Agents;
using Compendium.Core.Results;
using AwesomeAssertions;
using NSubstitute;

namespace Compendium.Abstractions.AI.Tests.Agents;

public sealed class StandardAgentTests
{
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly IAgentToolRegistry _tools = Substitute.For<IAgentToolRegistry>();

    private StandardAgent CreateAgent() => new(_provider, _tools);

    [Fact]
    public async Task RunAsync_NoToolsNoAction_ReturnsCompletedAfterOneTurn()
    {
        SetupProvider("Hello — final answer.", promptTokens: 10, completionTokens: 5);

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "Say hi",
            Model = "anthropic/claude-3.5-sonnet",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.TerminationReason.Should().Be(AgentTerminationReason.Completed);
        result.Value.Turns.Should().ContainSingle();
        result.Value.FinalOutput.Should().Be("Hello — final answer.");
        result.Value.TotalUsage.PromptTokens.Should().Be(10);
        result.Value.TotalUsage.CompletionTokens.Should().Be(5);
    }

    [Fact]
    public async Task RunAsync_WithToolCall_LoopsAndReturnsFinalAnswer()
    {
        // First turn: action calling 'echo'. Second turn: final answer (no action).
        var responses = new Queue<string>();
        responses.Enqueue("```action\n{\"tool\":\"echo\",\"args\":{\"text\":\"hi\"}}\n```");
        responses.Enqueue("Got the echo: hi");

        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => CompletionFromQueue(responses));

        _tools.InvokeAsync("echo", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AgentToolResult("echoed: hi")));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "echo hi",
            Model = "x",
            Tools = new[] { new AgentTool("echo", "echoes its input") },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.TerminationReason.Should().Be(AgentTerminationReason.Completed);
        result.Value.Turns.Should().HaveCount(2);
        result.Value.Turns[0].ToolInvocations.Should().ContainSingle()
            .Which.ToolName.Should().Be("echo");
        result.Value.FinalOutput.Should().Be("Got the echo: hi");
    }

    [Fact]
    public async Task RunAsync_HitsMaxTurns_TerminatesWithMaxTurnsReached()
    {
        // Always emit an action — so the loop never terminates organically.
        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Result.Success(new CompletionResponse
            {
                Id = "id",
                Model = "x",
                Content = "```action\n{\"tool\":\"echo\",\"args\":{}}\n```",
                FinishReason = FinishReason.ToolCall,
                Usage = new UsageStats { PromptTokens = 1, CompletionTokens = 1 },
            })));

        _tools.InvokeAsync("echo", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AgentToolResult("ok")));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "loop",
            Model = "x",
            Tools = new[] { new AgentTool("echo", "echoes") },
            Options = new AgentLoopOptions { MaxTurns = 3 },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.TerminationReason.Should().Be(AgentTerminationReason.MaxTurnsReached);
        result.Value.Turns.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunAsync_UnknownTool_FeedsErrorBack_DoesNotInvokeRegistry()
    {
        // First: action calling unknown tool. Second: final answer.
        var responses = new Queue<string>();
        responses.Enqueue("```action\n{\"tool\":\"nope\",\"args\":{}}\n```");
        responses.Enqueue("Sorry, can't help.");

        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => CompletionFromQueue(responses));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "do nope",
            Model = "x",
            Tools = new[] { new AgentTool("echo", "echoes") },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Turns[0].ToolInvocations.Should().ContainSingle()
            .Which.IsError.Should().BeTrue();
        await _tools.DidNotReceive()
            .InvokeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ToolReturnsError_FeedsBack_AndKeepsLooping()
    {
        var responses = new Queue<string>();
        responses.Enqueue("```action\n{\"tool\":\"flaky\",\"args\":{}}\n```");
        responses.Enqueue("I tried; it failed; here's the answer.");

        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => CompletionFromQueue(responses));

        _tools.InvokeAsync("flaky", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AgentToolResult("upstream 503", IsError: true)));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "try flaky",
            Model = "x",
            Tools = new[] { new AgentTool("flaky", "may fail") },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Turns.Should().HaveCount(2);
        result.Value.Turns[0].ToolInvocations.Should().ContainSingle()
            .Which.IsError.Should().BeTrue();
        result.Value.FinalOutput.Should().Contain("failed");
    }

    [Fact]
    public async Task RunAsync_RegistryFailure_AbortsLoop()
    {
        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new CompletionResponse
            {
                Id = "id",
                Model = "x",
                Content = "```action\n{\"tool\":\"echo\",\"args\":{}}\n```",
                FinishReason = FinishReason.ToolCall,
                Usage = new UsageStats { PromptTokens = 1, CompletionTokens = 1 },
            })));

        _tools.InvokeAsync("echo", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<AgentToolResult>(
                Error.Failure("Tools.RegistryBroken", "registry crashed"))));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "do",
            Model = "x",
            Tools = new[] { new AgentTool("echo", "echoes") },
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tools.RegistryBroken");
    }

    [Fact]
    public async Task RunAsync_TokenBudgetExhausted_TerminatesPartial()
    {
        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Success(new CompletionResponse
            {
                Id = "id",
                Model = "x",
                Content = "```action\n{\"tool\":\"echo\",\"args\":{}}\n```",
                FinishReason = FinishReason.ToolCall,
                Usage = new UsageStats { PromptTokens = 600, CompletionTokens = 600 },
            })));

        _tools.InvokeAsync("echo", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AgentToolResult("ok")));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "loop",
            Model = "x",
            Tools = new[] { new AgentTool("echo", "echoes") },
            Options = new AgentLoopOptions { MaxTurns = 10, MaxTotalTokens = 1000 },
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.TerminationReason.Should().Be(AgentTerminationReason.TokenBudgetExhausted);
    }

    [Fact]
    public async Task RunAsync_UserPromptMissing_ReturnsValidation()
    {
        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest { UserPrompt = "", Model = "x" });
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Agent.UserPromptRequired");
    }

    [Fact]
    public async Task RunAsync_ModelMissing_ReturnsValidation()
    {
        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest { UserPrompt = "x", Model = "" });
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Agent.ModelRequired");
    }

    [Fact]
    public async Task RunAsync_TimeoutZero_ReturnsValidation()
    {
        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "x",
            Model = "x",
            Options = new AgentLoopOptions { Timeout = TimeSpan.Zero },
        });
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Agent.TimeoutInvalid");
    }

    [Fact]
    public async Task RunAsync_NoToolsInRequest_FallsBackToRegistryDiscover()
    {
        // Arrange: registry advertises one tool; request.Tools is null.
        _tools.Discover().Returns(new[] { new AgentTool("only-from-registry", "the only tool") });

        // Provider responds with an action calling that tool, then a final answer.
        var responses = new Queue<string>();
        responses.Enqueue("```action\n{\"tool\":\"only-from-registry\",\"args\":{}}\n```");
        responses.Enqueue("done");
        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => CompletionFromQueue(responses));

        _tools.InvokeAsync("only-from-registry", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AgentToolResult("ok")));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "x",
            Model = "x",
            // No Tools field — registry should be consulted.
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Turns[0].ToolInvocations.Should().ContainSingle()
            .Which.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_NoToolsAtAll_UnknownToolMessageNotEmpty()
    {
        // Neither request nor registry advertises tools. The model still emits an
        // action — the unknown-tool feedback must not end with "Choose one of:".
        _tools.Discover().Returns(Array.Empty<AgentTool>());
        var responses = new Queue<string>();
        responses.Enqueue("```action\n{\"tool\":\"nope\",\"args\":{}}\n```");
        responses.Enqueue("ok then");
        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => CompletionFromQueue(responses));

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest { UserPrompt = "x", Model = "x" });

        result.IsSuccess.Should().BeTrue();
        var inv = result.Value.Turns[0].ToolInvocations.Should().ContainSingle().Subject;
        inv.IsError.Should().BeTrue();
        inv.ResultText.Should().Contain("No tools are available");
    }

    [Fact]
    public async Task RunAsync_OnTurnCompletedCallback_FiresPerTurn()
    {
        SetupProvider("done", 1, 1);
        var observed = new List<int>();

        var agent = CreateAgent();
        var result = await agent.RunAsync(new AgentRequest
        {
            UserPrompt = "x",
            Model = "x",
            Options = new AgentLoopOptions { OnTurnCompleted = t => observed.Add(t.Index) },
        });

        result.IsSuccess.Should().BeTrue();
        observed.Should().Equal(1);
    }

    private void SetupProvider(string content, int promptTokens, int completionTokens)
    {
        _provider.CompleteAsync(Arg.Any<CompletionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new CompletionResponse
            {
                Id = Guid.NewGuid().ToString(),
                Model = "x",
                Content = content,
                FinishReason = FinishReason.Stop,
                Usage = new UsageStats { PromptTokens = promptTokens, CompletionTokens = completionTokens },
            })));
    }

    private static Task<Result<CompletionResponse>> CompletionFromQueue(Queue<string> q)
    {
        var content = q.Dequeue();
        return Task.FromResult(Result.Success(new CompletionResponse
        {
            Id = Guid.NewGuid().ToString(),
            Model = "x",
            Content = content,
            FinishReason = content.Contains("```action") ? FinishReason.ToolCall : FinishReason.Stop,
            Usage = new UsageStats { PromptTokens = 5, CompletionTokens = 5 },
        }));
    }
}
