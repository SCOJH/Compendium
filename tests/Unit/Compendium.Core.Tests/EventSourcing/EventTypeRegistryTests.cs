// -----------------------------------------------------------------------
// <copyright file="EventTypeRegistryTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Reflection;
using Compendium.Core.EventSourcing;
using Compendium.Core.EventSourcing.Attributes;
using Compendium.Core.Tests.TestHelpers;

namespace Compendium.Core.Tests.EventSourcing;

public class EventTypeRegistryTests : IDisposable
{
    private readonly EventTypeRegistry _registry;

    public EventTypeRegistryTests()
    {
        _registry = new EventTypeRegistry();
    }

    [Fact]
    public void RegisterEventType_WithValidDomainEvent_RegistersSuccessfully()
    {
        // Arrange
        var eventType = typeof(TestDomainEvent);

        // Act
        _registry.RegisterEventType(eventType);

        // Assert
        var typeName = eventType.AssemblyQualifiedName!;
        _registry.IsWhitelisted(typeName).Should().BeTrue();
        _registry.GetWhitelistedType(typeName).Should().Be(eventType);
        _registry.Count.Should().Be(1);
    }

    [Fact]
    public void RegisterEventType_WithNonDomainEventType_ThrowsArgumentException()
    {
        // Arrange
        var nonEventType = typeof(string);

        // Act & Assert
        var action = () => _registry.RegisterEventType(nonEventType);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*must implement IDomainEvent*");
    }

    [Fact]
    public void RegisterEventType_WithNullType_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => _registry.RegisterEventType(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsWhitelisted_WithNonRegisteredType_ReturnsFalse()
    {
        // Arrange
        var typeName = typeof(TestDomainEvent).AssemblyQualifiedName!;

        // Act & Assert
        _registry.IsWhitelisted(typeName).Should().BeFalse();
    }

    [Fact]
    public void IsWhitelisted_WithNullOrEmptyTypeName_ThrowsArgumentException()
    {
        // Act & Assert
        var action1 = () => _registry.IsWhitelisted(null!);
        action1.Should().Throw<ArgumentException>();

        var action2 = () => _registry.IsWhitelisted("");
        action2.Should().Throw<ArgumentException>();

        var action3 = () => _registry.IsWhitelisted("   ");
        action3.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetWhitelistedType_WithNonRegisteredType_ReturnsNull()
    {
        // Arrange
        var typeName = typeof(TestDomainEvent).AssemblyQualifiedName!;

        // Act & Assert
        _registry.GetWhitelistedType(typeName).Should().BeNull();
    }

    [Fact]
    public void GetWhitelistedType_WithNullOrEmptyTypeName_ThrowsArgumentException()
    {
        // Act & Assert
        var action1 = () => _registry.GetWhitelistedType(null!);
        action1.Should().Throw<ArgumentException>();

        var action2 = () => _registry.GetWhitelistedType("");
        action2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterEventTypes_WithMultipleValidTypes_RegistersAll()
    {
        // Arrange
        var eventTypes = new[] { typeof(TestDomainEvent) };

        // Act
        _registry.RegisterEventTypes(eventTypes);

        // Assert
        _registry.Count.Should().Be(1);
        _registry.IsWhitelisted(typeof(TestDomainEvent).AssemblyQualifiedName!).Should().BeTrue();
    }

    [Fact]
    public void RegisterEventTypes_WithEmptyCollection_DoesNothing()
    {
        // Arrange
        var eventTypes = Array.Empty<Type>();

        // Act
        _registry.RegisterEventTypes(eventTypes);

        // Assert
        _registry.Count.Should().Be(0);
    }

    [Fact]
    public void RegisterEventTypes_WithInvalidType_ThrowsArgumentException()
    {
        // Arrange
        var eventTypes = new[] { typeof(string) };

        // Act & Assert
        var action = () => _registry.RegisterEventTypes(eventTypes);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*must implement IDomainEvent*");
    }

    [Fact]
    public void AutoRegisterFromAssemblies_WithValidAssembly_RegistersDomainEvents()
    {
        // Arrange
        var assembly = Assembly.GetExecutingAssembly();

        // Act
        _registry.AutoRegisterFromAssemblies(assembly);

        // Assert
        _registry.Count.Should().BeGreaterThan(0);
        _registry.IsWhitelisted(typeof(TestDomainEvent).AssemblyQualifiedName!).Should().BeTrue();
    }

    [Fact]
    public void AutoRegisterFromAssemblies_WithNullAssembly_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => _registry.AutoRegisterFromAssemblies(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetRegisteredTypes_ReturnsAllRegisteredTypes()
    {
        // Arrange
        _registry.RegisterEventType(typeof(TestDomainEvent));

        // Act
        var registeredTypes = _registry.GetRegisteredTypes();

        // Assert
        registeredTypes.Should().HaveCount(1);
        registeredTypes.Should().Contain(typeof(TestDomainEvent));
    }

    [Fact]
    public void Clear_RemovesAllRegisteredTypes()
    {
        // Arrange
        _registry.RegisterEventType(typeof(TestDomainEvent));
        _registry.Count.Should().Be(1);

        // Act
        _registry.Clear();

        // Assert
        _registry.Count.Should().Be(0);
        _registry.IsWhitelisted(typeof(TestDomainEvent).AssemblyQualifiedName!).Should().BeFalse();
    }

    [Fact]
    public void RegisterEventType_SameTypeTwice_OnlyRegistersOnce()
    {
        // Arrange
        var eventType = typeof(TestDomainEvent);

        // Act
        _registry.RegisterEventType(eventType);
        _registry.RegisterEventType(eventType);

        // Assert
        _registry.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        var eventTypes = new[] { typeof(TestDomainEvent) };

        // Act - Concurrent registration and reading
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => _registry.RegisterEventTypes(eventTypes)));
            tasks.Add(Task.Run(() => _registry.IsWhitelisted(typeof(TestDomainEvent).AssemblyQualifiedName!)));
            tasks.Add(Task.Run(() => _registry.GetRegisteredTypes()));
        }

        // Assert - All tasks complete without exceptions
        var aggregateTask = Task.WhenAll(tasks);
        await aggregateTask.WaitAsync(TimeSpan.FromSeconds(5));
        _registry.Count.Should().Be(1);
    }

    [Fact]
    public void Dispose_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _registry.Dispose();

        // Act & Assert
        var action1 = () => _registry.RegisterEventType(typeof(TestDomainEvent));
        action1.Should().Throw<ObjectDisposedException>();

        var action2 = () => _registry.IsWhitelisted("test");
        action2.Should().Throw<ObjectDisposedException>();

        var action3 = () => _registry.GetWhitelistedType("test");
        action3.Should().Throw<ObjectDisposedException>();

        var action4 = () => _registry.GetRegisteredTypes();
        action4.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void GetLogicalName_WithDecoratedType_ReturnsAttributeName()
    {
        // Act & Assert
        _registry.GetLogicalName(typeof(NamedTestDomainEvent))
            .Should().Be("EventTypeRegistryTests.Named");
    }

    [Fact]
    public void GetLogicalName_WithUndecoratedType_ReturnsAssemblyQualifiedName()
    {
        // Act & Assert - the value from before logical names existed, to the character
        _registry.GetLogicalName(typeof(TestDomainEvent))
            .Should().Be(typeof(TestDomainEvent).AssemblyQualifiedName);
    }

    [Fact]
    public void GetLogicalName_WithNullType_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => _registry.GetLogicalName(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetLogicalName_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _registry.Dispose();

        // Act & Assert
        var action = () => _registry.GetLogicalName(typeof(NamedTestDomainEvent));
        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void RegisterEventType_WithDecoratedType_ResolvesUnderBothNames()
    {
        // Arrange
        var eventType = typeof(NamedTestDomainEvent);
        var assemblyQualifiedName = eventType.AssemblyQualifiedName!;

        // Act
        _registry.RegisterEventType(eventType);

        // Assert - a log holding either form is readable by this single binary
        _registry.GetWhitelistedType("EventTypeRegistryTests.Named").Should().Be(eventType);
        _registry.GetWhitelistedType(assemblyQualifiedName).Should().Be(eventType);
        _registry.IsWhitelisted("EventTypeRegistryTests.Named").Should().BeTrue();
        _registry.IsWhitelisted(assemblyQualifiedName).Should().BeTrue();
    }

    [Fact]
    public void RegisterEventType_WithDecoratedType_CountsOneTypeNotTwoKeys()
    {
        // Act
        _registry.RegisterEventType(typeof(NamedTestDomainEvent));

        // Assert
        _registry.Count.Should().Be(1);
        _registry.GetRegisteredTypes().Should().HaveCount(1);
        _registry.GetRegisteredTypes().Should().Contain(typeof(NamedTestDomainEvent));
    }

    [Fact]
    public void RegisterEventTypes_WithDecoratedType_ResolvesUnderBothNames()
    {
        // Act
        _registry.RegisterEventTypes(new[] { typeof(NamedTestDomainEvent) });

        // Assert
        _registry.Count.Should().Be(1);
        _registry.GetWhitelistedType("EventTypeRegistryTests.Named").Should().Be(typeof(NamedTestDomainEvent));
        _registry.GetWhitelistedType(typeof(NamedTestDomainEvent).AssemblyQualifiedName!)
            .Should().Be(typeof(NamedTestDomainEvent));
    }

    [Fact]
    public void Clear_AfterDecoratedRegistration_RemovesBothNames()
    {
        // Arrange
        _registry.RegisterEventType(typeof(NamedTestDomainEvent));

        // Act
        _registry.Clear();

        // Assert
        _registry.Count.Should().Be(0);
        _registry.IsWhitelisted("EventTypeRegistryTests.Named").Should().BeFalse();
        _registry.IsWhitelisted(typeof(NamedTestDomainEvent).AssemblyQualifiedName!).Should().BeFalse();
    }

    [Fact]
    public void RegisterEventType_TwoTypesClaimingTheSameLogicalName_Throws()
    {
        // Arrange
        _registry.RegisterEventType(typeof(FirstDuplicatedNameEvent));

        // Act & Assert - a "last one wins" would deserialize a payload into the wrong type
        var action = () => _registry.RegisterEventType(typeof(SecondDuplicatedNameEvent));
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(FirstDuplicatedNameEvent).FullName}*")
            .WithMessage($"*{typeof(SecondDuplicatedNameEvent).FullName}*");

        _registry.Count.Should().Be(1);
        _registry.GetWhitelistedType("EventTypeRegistryTests.Duplicated")
            .Should().Be(typeof(FirstDuplicatedNameEvent));
    }

    [Fact]
    public void RegisterEventTypes_WithCollidingBatch_RegistersNothing()
    {
        // Arrange
        var colliding = new[] { typeof(FirstDuplicatedNameEvent), typeof(SecondDuplicatedNameEvent) };

        // Act & Assert - the whole batch is validated before a single write
        var action = () => _registry.RegisterEventTypes(colliding);
        action.Should().Throw<InvalidOperationException>();

        _registry.Count.Should().Be(0);
        _registry.IsWhitelisted("EventTypeRegistryTests.Duplicated").Should().BeFalse();
    }

    [Fact]
    public void RegisterEventType_SameDecoratedTypeTwice_DoesNotThrow()
    {
        // Act
        _registry.RegisterEventType(typeof(NamedTestDomainEvent));
        var action = () => _registry.RegisterEventType(typeof(NamedTestDomainEvent));

        // Assert
        action.Should().NotThrow();
        _registry.Count.Should().Be(1);
    }

    public void Dispose()
    {
        _registry?.Dispose();
    }
}

/// <summary>
/// A decorated domain event. Its logical name is unique in this assembly, so that
/// AutoRegisterFromAssemblies, which scans every concrete IDomainEvent of the test
/// assembly, keeps registering it without colliding.
/// </summary>
[EventTypeName("EventTypeRegistryTests.Named")]
public sealed class NamedTestDomainEvent : DomainEventBase
{
    public NamedTestDomainEvent()
        : base("aggregate", "Aggregate", 1)
    {
    }
}

/// <summary>
/// First half of the collision pair. Abstract on purpose: AutoRegisterFromAssemblies skips
/// abstract types, so the two types below can share a logical name — which is the point of
/// the collision tests — without making the assembly-wide scan of
/// AutoRegisterFromAssemblies_WithValidAssembly_RegistersDomainEvents throw.
/// </summary>
[EventTypeName("EventTypeRegistryTests.Duplicated")]
public abstract class FirstDuplicatedNameEvent : DomainEventBase
{
    protected FirstDuplicatedNameEvent()
        : base("aggregate", "Aggregate", 1)
    {
    }
}

/// <summary>
/// Second half of the collision pair. See <see cref="FirstDuplicatedNameEvent"/>.
/// </summary>
[EventTypeName("EventTypeRegistryTests.Duplicated")]
public abstract class SecondDuplicatedNameEvent : DomainEventBase
{
    protected SecondDuplicatedNameEvent()
        : base("aggregate", "Aggregate", 1)
    {
    }
}
