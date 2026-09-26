// -----------------------------------------------------------------------
// <copyright file="QueryDispatcherIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using Compendium.Abstractions.CQRS.Handlers;
using Compendium.Abstractions.CQRS.Queries;
using Compendium.Application.CQRS;
using Compendium.Core.Results;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Compendium.IntegrationTests.CQRS;

/// <summary>
/// Integration tests for the QueryDispatcher.
/// Tests query execution, handler resolution, error handling, and telemetry.
/// </summary>
public sealed class QueryDispatcherIntegrationTests
{
    [Fact]
    public async Task QueryDispatcher_WithValidQuery_ShouldExecuteHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();
        var query = new TestQuery { Filter = "test", PageSize = 10 };

        // Act
        var result = await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.TotalCount.Should().Be(42);
        result.Value.Items.Should().ContainSingle(item => item == "Result: test");

        // Verify the handler was called
        var handler = serviceProvider.GetRequiredService<IQueryHandler<TestQuery, TestQueryResult>>();
        await handler.Received(1).HandleAsync(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryDispatcher_WithNullQuery_ShouldReturnValidationError()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();

        // Act
        var result = await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Query.Null");
        result.Error.Message.Should().Contain("cannot be null");
    }

    [Fact]
    public async Task QueryDispatcher_WithMissingHandler_ShouldReturnHandlerNotFoundError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
        // Intentionally NOT registering a handler

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();
        var query = new TestQuery { Filter = "test", PageSize = 10 };

        // Act
        var result = await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(query);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Handler.NotFound");
        result.Error.Message.Should().Contain("No handler found");
    }

    [Fact]
    public async Task QueryDispatcher_WithHandlerException_ShouldHandleGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

        // Configure handler to throw exception
        var handler = Substitute.For<IQueryHandler<TestQuery, TestQueryResult>>();
        handler.When(h => h.HandleAsync(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Database connection failed"));

        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();
        var query = new TestQuery { Filter = "test", PageSize = 10 };

        // Act
        var result = await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(query);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Query.ExecutionFailed");
        result.Error.Message.Should().Contain("Database connection failed");
    }

    [Fact]
    public async Task QueryDispatcher_WithHandlerReturningFailure_ShouldPropagateError()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);

        // Override handler to return failure
        var handler = Substitute.For<IQueryHandler<TestQuery, TestQueryResult>>();
        handler.HandleAsync(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TestQueryResult>(Error.NotFound("Data.NotFound", "No data matching criteria")));

        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();
        var query = new TestQuery { Filter = "nonexistent", PageSize = 10 };

        // Act
        var result = await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(query);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Data.NotFound");
        result.Error.Message.Should().Contain("No data matching criteria");
    }

    [Fact]
    public async Task QueryDispatcher_ConcurrentQueries_ShouldExecuteAllSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();
        var queries = Enumerable.Range(0, 10).Select(i =>
            new TestQuery { Filter = $"test-{i}", PageSize = 10 }).ToArray();

        // Act - Execute queries concurrently
        var tasks = queries.Select(q =>
            dispatcher.DispatchAsync<TestQuery, TestQueryResult>(q)).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Should().AllSatisfy(r => r.Value.TotalCount.Should().Be(42));
    }

    [Fact]
    public async Task QueryDispatcher_WithPaginatedQuery_ShouldReturnCorrectPage()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();
        var query = new TestQuery { Filter = "paginated", PageSize = 5, PageNumber = 2 };

        // Act
        var result = await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.PageNumber.Should().Be(2);
        result.Value.PageSize.Should().Be(5);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Register logging
        services.AddLogging(builder => builder.AddConsole());

        // Register query dispatcher
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

        // Register test handler
        var handler = Substitute.For<IQueryHandler<TestQuery, TestQueryResult>>();
        handler.HandleAsync(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var query = callInfo.Arg<TestQuery>();
                var result = new TestQueryResult
                {
                    TotalCount = 42,
                    Items = new List<string> { $"Result: {query.Filter}" },
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize
                };
                return Result.Success(result);
            });
        services.AddSingleton(handler);
    }

    /// <summary>
    /// Test query for dispatcher testing.
    /// </summary>
    public sealed class TestQuery : IQuery<TestQueryResult>
    {
        [Required(ErrorMessage = "Filter is required")]
        public string Filter { get; set; } = string.Empty;

        [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100")]
        public int PageSize { get; set; } = 10;

        public int PageNumber { get; set; } = 1;
    }

    /// <summary>
    /// Test query result.
    /// </summary>
    public sealed class TestQueryResult
    {
        public int TotalCount { get; set; }
        public List<string> Items { get; set; } = new();
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
