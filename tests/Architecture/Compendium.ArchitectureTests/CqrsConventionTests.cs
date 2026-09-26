// -----------------------------------------------------------------------
// <copyright file="CqrsConventionTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Compendium.ArchitectureTests;

/// <summary>
/// Enforces CQRS conventions inside the Compendium framework.
///
/// The framework only ships the abstractions and the dispatchers; consumer code
/// implements the concrete <c>ICommand</c>/<c>IQuery</c> records. The rules below
/// guarantee that the types Compendium itself ships continue to honour the contract:
/// <list type="bullet">
///   <item><description>The marker interfaces (<c>ICommand</c>, <c>IQuery</c>) live where consumers expect them.</description></item>
///   <item><description>Dispatchers are sealed, in the expected namespace, and are the only public coordination types.</description></item>
///   <item><description>Pipeline behaviors live under <c>Compendium.Application.CQRS.Behaviors</c>.</description></item>
///   <item><description>Any framework-internal command/query handler implementation is sealed and stateless-by-convention.</description></item>
/// </list>
/// </summary>
public class CqrsConventionTests
{
    [Fact]
    public void CommandMarkerInterface_ShouldLiveIn_AbstractionsCommandsNamespace()
    {
        // Arrange / Act
        var commandInterface = AssemblyAnchors.Abstractions
            .GetType("Compendium.Abstractions.CQRS.Commands.ICommand");

        // Assert
        commandInterface.Should().NotBeNull(
            "the framework ships ICommand under Compendium.Abstractions.CQRS.Commands so consumers can locate it predictably");
        commandInterface!.IsInterface.Should().BeTrue("ICommand must be a marker interface");
    }

    [Fact]
    public void QueryMarkerInterface_ShouldLiveIn_AbstractionsQueriesNamespace()
    {
        // Arrange / Act
        var queryInterface = AssemblyAnchors.Abstractions
            .GetType("Compendium.Abstractions.CQRS.Queries.IQuery`1");

        // Assert
        queryInterface.Should().NotBeNull(
            "the framework ships IQuery<TResponse> under Compendium.Abstractions.CQRS.Queries");
        queryInterface!.IsInterface.Should().BeTrue("IQuery must be a marker interface");
    }

    [Fact]
    public void Dispatchers_ShouldBe_Sealed()
    {
        // Arrange
        var applicationAssembly = AssemblyAnchors.Application;

        // Act
        var result = Types.InAssembly(applicationAssembly)
            .That()
            .ResideInNamespace("Compendium.Application.CQRS")
            .And()
            .HaveNameEndingWith("Dispatcher")
            .And()
            .AreClasses()
            .And()
            .ArePublic()
            .Should()
            .BeSealed()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "command/query dispatchers are leaf types and must not be subclassed; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Dispatchers_ShouldHaveCorresponding_Interface()
    {
        // Arrange
        var commandDispatcher = AssemblyAnchors.Application
            .GetType("Compendium.Application.CQRS.CommandDispatcher");
        var commandDispatcherInterface = AssemblyAnchors.Application
            .GetType("Compendium.Application.CQRS.ICommandDispatcher");
        var queryDispatcher = AssemblyAnchors.Application
            .GetType("Compendium.Application.CQRS.QueryDispatcher");
        var queryDispatcherInterface = AssemblyAnchors.Application
            .GetType("Compendium.Application.CQRS.IQueryDispatcher");

        // Act / Assert
        commandDispatcher.Should().NotBeNull("CommandDispatcher is the framework entry point for command execution");
        commandDispatcherInterface.Should().NotBeNull("dispatchers expose an interface so consumers can substitute their own");
        commandDispatcherInterface!.IsAssignableFrom(commandDispatcher).Should().BeTrue();

        queryDispatcher.Should().NotBeNull("QueryDispatcher is the framework entry point for query execution");
        queryDispatcherInterface.Should().NotBeNull();
        queryDispatcherInterface!.IsAssignableFrom(queryDispatcher).Should().BeTrue();
    }

    [Fact]
    public void PipelineBehaviors_ShouldLiveIn_BehaviorsNamespace()
    {
        // Arrange
        var applicationAssembly = AssemblyAnchors.Application;

        // Act — every type whose name ends in "Behavior" inside Application must live in the conventional namespace.
        var result = Types.InAssembly(applicationAssembly)
            .That()
            .HaveNameEndingWith("Behavior")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespace("Compendium.Application.CQRS.Behaviors")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "pipeline behaviors are discoverable under Compendium.Application.CQRS.Behaviors; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void CommandHandlerInterface_DoesNotExposeMutableProperties()
    {
        // Arrange — handlers should be a verb-shaped contract, never a data carrier.
        var handlerInterface = AssemblyAnchors.Abstractions
            .GetType("Compendium.Abstractions.CQRS.Handlers.ICommandHandler`1");

        // Act / Assert
        handlerInterface.Should().NotBeNull();
        var setters = handlerInterface!.GetProperties()
            .Where(p => p.GetSetMethod() is not null)
            .Select(p => p.Name)
            .ToArray();

        setters.Should().BeEmpty(
            "ICommandHandler is a behaviour contract; it must not expose mutable properties");
    }
}
