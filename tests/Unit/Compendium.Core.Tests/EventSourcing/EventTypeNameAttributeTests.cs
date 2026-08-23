// -----------------------------------------------------------------------
// <copyright file="EventTypeNameAttributeTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Reflection;
using Compendium.Core.EventSourcing.Attributes;

namespace Compendium.Core.Tests.EventSourcing;

public class EventTypeNameAttributeTests
{
    [Fact]
    public void Constructor_WithValidName_ExposesIt()
    {
        // Act
        var attribute = new EventTypeNameAttribute("ConfigurationCreated");

        // Assert
        attribute.Name.Should().Be("ConfigurationCreated");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_WithEmptyOrWhitespaceName_ThrowsArgumentException(string name)
    {
        // Act & Assert
        var action = () => new EventTypeNameAttribute(name);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNullName_ThrowsArgumentException()
    {
        // Act & Assert
        var action = () => new EventTypeNameAttribute(null!);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Attribute_OnDecoratedType_IsReadableByReflection()
    {
        // Act
        var attribute = typeof(DecoratedForAttributeTests).GetCustomAttribute<EventTypeNameAttribute>(inherit: false);

        // Assert
        attribute.Should().NotBeNull();
        attribute!.Name.Should().Be("EventTypeNameAttributeTests.Decorated");
    }

    [Fact]
    public void Attribute_IsNotInherited()
    {
        // Act - a derived type must never claim the logical name of its parent
        var attribute = typeof(DerivedFromDecorated).GetCustomAttribute<EventTypeNameAttribute>(inherit: true);

        // Assert
        attribute.Should().BeNull();
    }

    [Fact]
    public void Attribute_AllowsClassesAndStructs_AndForbidsMultiple()
    {
        // Act
        var usage = typeof(EventTypeNameAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        // Assert
        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Struct);
        usage.AllowMultiple.Should().BeFalse();
        usage.Inherited.Should().BeFalse();
    }

    // Deliberately not domain events: EventTypeRegistry.AutoRegisterFromAssemblies scans this
    // assembly for every concrete IDomainEvent, and these types exist only to be reflected on.
    [EventTypeName("EventTypeNameAttributeTests.Decorated")]
    private class DecoratedForAttributeTests
    {
    }

    private sealed class DerivedFromDecorated : DecoratedForAttributeTests
    {
    }
}
