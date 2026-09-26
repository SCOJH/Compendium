// -----------------------------------------------------------------------
// <copyright file="HexagonalLayeringTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using AwesomeAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Compendium.ArchitectureTests;

/// <summary>
/// Enforces the hexagonal (ports-and-adapters) layering invariants of the Compendium framework.
///
/// Allowed dependency direction:
/// <list type="bullet">
///   <item><description><c>Compendium.Core</c> — zero outbound dependencies (pure domain primitives).</description></item>
///   <item><description><c>Compendium.Abstractions</c> — depends on Core only (port definitions).</description></item>
///   <item><description><c>Compendium.Application</c> — depends on Core + Abstractions; never on Infrastructure or Adapters.</description></item>
///   <item><description><c>Compendium.Infrastructure</c> — depends on Core + Abstractions (+ Application for orchestration); never on Adapters.</description></item>
///   <item><description><c>Compendium.Adapters.*</c> — outermost ring, may depend on inner rings; siblings stay independent.</description></item>
/// </list>
/// </summary>
public class HexagonalLayeringTests
{
    [Fact]
    public void Core_ShouldNotDependOn_Abstractions()
    {
        // Arrange
        var coreAssembly = AssemblyAnchors.Core;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .Should()
            .NotHaveDependencyOn("Compendium.Abstractions")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Core is the innermost ring and must remain dependency-free; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Core_ShouldNotDependOn_Application()
    {
        // Arrange
        var coreAssembly = AssemblyAnchors.Core;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .Should()
            .NotHaveDependencyOn("Compendium.Application")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Core must not depend on Application; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Core_ShouldNotDependOn_Infrastructure()
    {
        // Arrange
        var coreAssembly = AssemblyAnchors.Core;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .Should()
            .NotHaveDependencyOn("Compendium.Infrastructure")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Core must not depend on Infrastructure; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Core_ShouldNotDependOn_AnyAdapter()
    {
        // Arrange
        var coreAssembly = AssemblyAnchors.Core;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .Should()
            .NotHaveDependencyOn(AssemblyAnchors.AdapterNamespacePrefix)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Core must never reach outward into adapters; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Abstractions_ShouldNotDependOn_Application()
    {
        // Arrange
        var abstractionsAssembly = AssemblyAnchors.Abstractions;

        // Act
        var result = Types.InAssembly(abstractionsAssembly)
            .Should()
            .NotHaveDependencyOn("Compendium.Application")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Abstractions are ports — they must not depend on Application orchestration; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Abstractions_ShouldNotDependOn_Infrastructure()
    {
        // Arrange
        var abstractionsAssembly = AssemblyAnchors.Abstractions;

        // Act
        var result = Types.InAssembly(abstractionsAssembly)
            .Should()
            .NotHaveDependencyOn("Compendium.Infrastructure")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Abstractions must not depend on concrete Infrastructure; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Abstractions_ShouldNotDependOn_AnyAdapter()
    {
        // Arrange
        var abstractionsAssembly = AssemblyAnchors.Abstractions;

        // Act
        var result = Types.InAssembly(abstractionsAssembly)
            .Should()
            .NotHaveDependencyOn(AssemblyAnchors.AdapterNamespacePrefix)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Abstractions are inbound from adapters, never outbound; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        // Arrange
        var applicationAssembly = AssemblyAnchors.Application;

        // Act
        var result = Types.InAssembly(applicationAssembly)
            .Should()
            .NotHaveDependencyOn("Compendium.Infrastructure")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Application must talk to Infrastructure only through Abstractions; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Application_ShouldNotDependOn_AnyAdapter()
    {
        // Arrange
        var applicationAssembly = AssemblyAnchors.Application;

        // Act
        var result = Types.InAssembly(applicationAssembly)
            .Should()
            .NotHaveDependencyOn(AssemblyAnchors.AdapterNamespacePrefix)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Application must never reference concrete adapters; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOn_AnyAdapter()
    {
        // Arrange
        var infrastructureAssembly = AssemblyAnchors.Infrastructure;

        // Act
        var result = Types.InAssembly(infrastructureAssembly)
            .Should()
            .NotHaveDependencyOn(AssemblyAnchors.AdapterNamespacePrefix)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Infrastructure provides shared plumbing — adapters depend on it, never the reverse; offending types: {0}",
            string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }
}
