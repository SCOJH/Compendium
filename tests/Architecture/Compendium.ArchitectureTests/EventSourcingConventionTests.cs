// -----------------------------------------------------------------------
// <copyright file="EventSourcingConventionTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Reflection;
using Compendium.Core.Domain.Events;
using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Compendium.ArchitectureTests;

/// <summary>
/// Enforces event-sourcing conventions inside the Compendium framework.
///
/// Domain events are the immutable history of an aggregate; once written, they must
/// never mutate. Anything that ships in <c>Compendium.Core</c> as a domain event,
/// or that consumers will derive from, must therefore expose only init-only or
/// read-only state.
/// </summary>
public class EventSourcingConventionTests
{
    [Fact]
    public void IDomainEvent_ShouldOnlyExpose_ReadOnlyProperties()
    {
        // Arrange
        var domainEventType = AssemblyAnchors.DomainEventInterface;

        // Act
        var settableProperties = domainEventType.GetProperties()
            .Where(p => p.GetSetMethod() is not null)
            .Select(p => p.Name)
            .ToArray();

        // Assert
        settableProperties.Should().BeEmpty(
            "IDomainEvent represents an immutable historical fact and must expose only read-only properties");
    }

    [Fact]
    public void DomainEventBase_ShouldNotExpose_PublicSettableProperties()
    {
        // Arrange — an event's payload must be fixed at construction time.
        var domainEventBaseType = typeof(DomainEventBase);

        // Act
        var publicSetters = domainEventBaseType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
            {
                var setter = p.GetSetMethod(nonPublic: false);
                return setter is not null && setter.IsPublic;
            })
            .Select(p => p.Name)
            .ToArray();

        // Assert
        publicSetters.Should().BeEmpty(
            "DomainEventBase backs every event in the system; public setters would let downstream code mutate history");
    }

    [Fact]
    public void DomainEventBase_ShouldBe_Abstract()
    {
        // Arrange
        var domainEventBaseType = typeof(DomainEventBase);

        // Act / Assert
        domainEventBaseType.IsAbstract.Should().BeTrue(
            "DomainEventBase is a template that consumers extend per concrete event");
    }

    [Fact]
    public void DomainEventBase_ShouldImplement_IDomainEvent()
    {
        // Arrange
        var domainEventBaseType = typeof(DomainEventBase);

        // Act / Assert
        AssemblyAnchors.DomainEventInterface
            .IsAssignableFrom(domainEventBaseType)
            .Should().BeTrue("DomainEventBase is the canonical IDomainEvent implementation");
    }

    [Fact]
    public void IDomainEvent_ShouldLiveIn_CoreDomainEventsNamespace()
    {
        // Arrange
        var domainEventType = AssemblyAnchors.DomainEventInterface;

        // Act / Assert
        domainEventType.Namespace.Should().Be(
            "Compendium.Core.Domain.Events",
            "the domain-event marker is part of the inner ring and must remain in Core");
    }

    [Fact]
    public void DomainEventBase_ShouldNotDependOn_AnyAdapter()
    {
        // Arrange
        var coreAssembly = AssemblyAnchors.Core;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .That()
            .ResideInNamespace("Compendium.Core.Domain.Events")
            .Should()
            .NotHaveDependencyOn(AssemblyAnchors.AdapterNamespacePrefix)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "domain events are persistence-agnostic and must not reach into adapters; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void EventStoreInterface_ShouldLiveIn_AbstractionsNamespace()
    {
        // Arrange / Act
        var eventStoreInterface = AssemblyAnchors.Abstractions
            .GetType("Compendium.Abstractions.EventSourcing.IEventStore");

        // Assert
        eventStoreInterface.Should().NotBeNull(
            "the event-store port is contractual and lives in Compendium.Abstractions.EventSourcing so adapters can implement it");
        eventStoreInterface!.IsInterface.Should().BeTrue();
    }
}
