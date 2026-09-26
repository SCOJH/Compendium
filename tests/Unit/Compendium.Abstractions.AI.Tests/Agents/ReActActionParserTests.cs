// -----------------------------------------------------------------------
// <copyright file="ReActActionParserTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Application.AI.Agents;
using AwesomeAssertions;

namespace Compendium.Abstractions.AI.Tests.Agents;

public sealed class ReActActionParserTests
{
    [Fact]
    public void TryParse_NoActionBlock_ReturnsFalse_NoError()
    {
        var ok = ReActActionParser.TryParse("Just a final answer.", out var action, out var err);
        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().BeNull();
    }

    [Fact]
    public void TryParse_ValidAction_ParsesToolAndArgs()
    {
        var content = """
        Some preamble.

        ```action
        {"tool": "echo", "args": {"text": "hello"}}
        ```

        Some trailing prose.
        """;

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeTrue();
        err.Should().BeNull();
        action.Should().NotBeNull();
        action!.ToolName.Should().Be("echo");
        action.ArgumentsJson.Should().Contain("\"text\"");
        action.ArgumentsJson.Should().Contain("\"hello\"");
    }

    [Fact]
    public void TryParse_ValidAction_NoArgsField_DefaultsToEmptyObject()
    {
        var content = "```action\n{\"tool\": \"ping\"}\n```";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeTrue();
        action!.ArgumentsJson.Should().Be("{}");
        err.Should().BeNull();
    }

    [Fact]
    public void TryParse_MissingTool_ReturnsParseError()
    {
        var content = "```action\n{\"args\": {\"x\": 1}}\n```";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().Contain("tool");
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsParseError()
    {
        var content = "```action\n{\"tool\": \"echo\", args: bad}\n```";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().NotBeNull();
        err!.ToLowerInvariant().Should().Contain("malformed");
    }

    [Fact]
    public void TryParse_UnclosedBlock_ReturnsParseError()
    {
        var content = "Here we go:\n```action\n{\"tool\": \"echo\"}\nthe end";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().Contain("not closed");
    }

    [Fact]
    public void TryParse_ToolEmptyString_ReturnsParseError()
    {
        var content = "```action\n{\"tool\": \"\"}\n```";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().Contain("non-empty");
    }

    [Fact]
    public void TryParse_ArgsAsString_ReturnsParseError()
    {
        var content = "```action\n{\"tool\": \"echo\", \"args\": \"not an object\"}\n```";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().Contain("must be a JSON object");
    }

    [Fact]
    public void TryParse_ActionInsidePostfix_NotConfusedWithActionable()
    {
        // "actionable" must NOT match "action".
        var content = "I'll do this in an actionable way: just answer.";
        var ok = ReActActionParser.TryParse(content, out var action, out var err);
        ok.Should().BeFalse();
        action.Should().BeNull();
        err.Should().BeNull();
    }

    [Fact]
    public void TryParse_InlineFormat_OnSingleLine_Succeeds()
    {
        // Some models emit ```action {"tool":"x"} ``` without a leading newline.
        var content = "Trying it: ```action {\"tool\":\"echo\",\"args\":{\"text\":\"hi\"}}```";

        var ok = ReActActionParser.TryParse(content, out var action, out var err);

        ok.Should().BeTrue();
        err.Should().BeNull();
        action.Should().NotBeNull();
        action!.ToolName.Should().Be("echo");
    }
}
