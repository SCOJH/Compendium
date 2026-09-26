// -----------------------------------------------------------------------
// <copyright file="NamingConventionTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Compendium.ArchitectureTests;

/// <summary>
/// Enforces naming conventions across the Compendium framework.
///
/// Predictable names make the framework discoverable: a consumer skimming the
/// public API can locate the right port or extension by namespace + suffix alone.
/// </summary>
public class NamingConventionTests
{
    [Fact]
    public void Interfaces_ShouldStartWith_CapitalI()
    {
        // Arrange
        var assemblies = new[]
        {
            AssemblyAnchors.Core,
            AssemblyAnchors.Abstractions,
            AssemblyAnchors.Application,
            AssemblyAnchors.Infrastructure,
        };

        foreach (var assembly in assemblies)
        {
            // Act
            var result = Types.InAssembly(assembly)
                .That()
                .AreInterfaces()
                .And()
                .ArePublic()
                .Should()
                .HaveNameStartingWith("I")
                .GetResult();

            // Assert
            result.IsSuccessful.Should().BeTrue(
                "all public interfaces in {0} must follow the .NET 'I'-prefix convention; offending types: {1}",
                assembly.GetName().Name,
                string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
        }
    }

    [Fact]
    public void Dispatcher_Types_ShouldLiveIn_CqrsNamespace()
    {
        // Arrange
        var applicationAssembly = AssemblyAnchors.Application;

        // Act
        var result = Types.InAssembly(applicationAssembly)
            .That()
            .HaveNameEndingWith("Dispatcher")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespace("Compendium.Application.CQRS")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "every *Dispatcher class is part of the CQRS coordination layer; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Behavior_Types_ShouldLiveIn_BehaviorsNamespace()
    {
        // Arrange
        var applicationAssembly = AssemblyAnchors.Application;

        // Act
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
            "*Behavior types are pipeline behaviors and must live under CQRS.Behaviors; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void EventStoreImplementations_ShouldLiveIn_EventSourcingNamespace()
    {
        // Arrange
        var infrastructureAssembly = AssemblyAnchors.Infrastructure;

        // Act
        var result = Types.InAssembly(infrastructureAssembly)
            .That()
            .HaveNameEndingWith("EventStore")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespaceStartingWith("Compendium.Infrastructure.EventSourcing")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "concrete *EventStore types live under Infrastructure.EventSourcing for discoverability; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void ProjectionTypes_ShouldLiveIn_ProjectionsNamespace()
    {
        // Arrange
        var infrastructureAssembly = AssemblyAnchors.Infrastructure;

        // Act
        var result = Types.InAssembly(infrastructureAssembly)
            .That()
            .HaveNameEndingWith("Projection")
            .And()
            .AreClasses()
            .And()
            .ArePublic()
            .Should()
            .ResideInNamespaceStartingWith("Compendium.Infrastructure.Projections")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "public *Projection classes belong under Infrastructure.Projections; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void DomainEvents_AssemblyDefined_ShouldLiveIn_CoreDomainEventsNamespace()
    {
        // Arrange — Core ships only the *base* event types; verify they all live in the documented folder.
        var coreAssembly = AssemblyAnchors.Core;
        var domainEventInterface = AssemblyAnchors.DomainEventInterface;

        // Act
        var typesImplementingDomainEvent = coreAssembly
            .GetTypes()
            .Where(t => domainEventInterface.IsAssignableFrom(t) && t != domainEventInterface)
            .Where(t => !t.Name.Contains('<')) // skip compiler-generated closures
            .ToArray();

        // Assert — every public/internal IDomainEvent in Core lives where consumers can find it.
        foreach (var type in typesImplementingDomainEvent)
        {
            type.Namespace.Should().StartWith(
                "Compendium.Core.Domain.Events",
                "{0} implements IDomainEvent and must live under Compendium.Core.Domain.Events",
                type.FullName);
        }
    }

    [Fact]
    public void IntegrationEvents_ShouldLiveIn_DomainEventsNamespace()
    {
        // Arrange
        var coreAssembly = AssemblyAnchors.Core;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .That()
            .HaveNameEndingWith("IntegrationEvent")
            .Or()
            .HaveNameEndingWith("IntegrationEventBase")
            .Should()
            .ResideInNamespaceStartingWith("Compendium.Core.Domain.Events")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "*IntegrationEvent types live alongside IDomainEvent under Core.Domain.Events; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }
}
