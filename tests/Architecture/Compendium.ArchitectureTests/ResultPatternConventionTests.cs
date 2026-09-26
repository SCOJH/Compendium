// -----------------------------------------------------------------------
// <copyright file="ResultPatternConventionTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Reflection;
using Compendium.Core.Results;
using AwesomeAssertions;
using Xunit;

namespace Compendium.ArchitectureTests;

/// <summary>
/// Enforces structural conventions of the Result pattern.
///
/// The Result pattern is the framework's contract for representing operation outcomes
/// without throwing exceptions for control flow. Anything Compendium ships that
/// claims to "return a Result" must expose the standard <c>IsSuccess</c>/<c>IsFailure</c>
/// surface that the rest of the codebase asserts against.
/// </summary>
public class ResultPatternConventionTests
{
    [Fact]
    public void Result_ShouldExpose_IsSuccessAndIsFailureProperties()
    {
        // Arrange
        var resultType = typeof(Result);

        // Act
        var isSuccess = resultType.GetProperty("IsSuccess", BindingFlags.Public | BindingFlags.Instance);
        var isFailure = resultType.GetProperty("IsFailure", BindingFlags.Public | BindingFlags.Instance);
        var error = resultType.GetProperty("Error", BindingFlags.Public | BindingFlags.Instance);

        // Assert
        isSuccess.Should().NotBeNull("Result must publicly expose IsSuccess");
        isSuccess!.PropertyType.Should().Be<bool>();

        isFailure.Should().NotBeNull("Result must publicly expose IsFailure");
        isFailure!.PropertyType.Should().Be<bool>();

        error.Should().NotBeNull("Result must publicly expose its Error");
    }

    [Fact]
    public void Result_PublicProperties_ShouldNotHavePublicSetters()
    {
        // Arrange — Result is a value-like outcome; its state must be fixed at construction.
        var resultType = typeof(Result);

        // Act
        var publicSetters = resultType
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
            "Result is observed by callers and must be immutable from the outside");
    }

    [Fact]
    public void GenericResult_ShouldBe_Sealed()
    {
        // Arrange
        var genericResultType = typeof(Result<>);

        // Act / Assert
        genericResultType.IsSealed.Should().BeTrue(
            "Result<T> is a closed sum type — adding behaviour belongs in extension methods, not subclasses");
    }

    [Fact]
    public void GenericResult_ShouldInheritFrom_NonGenericResult()
    {
        // Arrange
        var genericResultType = typeof(Result<>);

        // Act / Assert
        genericResultType.BaseType.Should().NotBeNull();
        genericResultType.BaseType!.Should().Be(typeof(Result),
            "Result<T> must specialise the non-generic Result so success/failure semantics are uniform");
    }

    [Fact]
    public void GenericResult_PublicProperties_ShouldNotHavePublicSetters()
    {
        // Arrange
        var genericResultType = typeof(Result<>);

        // Act
        var publicSetters = genericResultType
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
            "Result<T> values are immutable observations of an operation's outcome");
    }

    [Fact]
    public void Error_ShouldLiveIn_ResultsNamespace()
    {
        // Arrange / Act
        var errorType = typeof(Error);

        // Assert
        errorType.Namespace.Should().Be("Compendium.Core.Results",
            "Error is part of the Result pattern and lives next to Result");
    }

    [Fact]
    [Trait("Category", "Heuristic")]
    public void Result_ShouldExposeStaticFactoryMethods_SuccessAndFailure()
    {
        // Arrange — heuristic: the documented Result API is `Result.Success(...)` and `Result.Failure(...)`.
        var resultType = typeof(Result);

        // Act
        var staticMethods = resultType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .Distinct()
            .ToArray();

        // Assert
        staticMethods.Should().Contain("Success",
            "Result.Success is the documented happy-path factory");
        staticMethods.Should().Contain("Failure",
            "Result.Failure is the documented failure-path factory");
    }
}
