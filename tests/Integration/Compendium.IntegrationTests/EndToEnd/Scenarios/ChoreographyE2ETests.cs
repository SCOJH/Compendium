// -----------------------------------------------------------------------
// <copyright file="ChoreographyE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Sagas.Choreography;
using Compendium.Application.Sagas.Choreography;
using Compendium.Application.Sagas.DependencyInjection;
using Compendium.Core.Domain.Events;
using Compendium.Core.Results;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E coverage for the new event-driven saga (Choreography) API. Verifies that
/// publishing one event triggers the appropriate handlers and that compensation
/// flows propagate correctly through a chain.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Saga")]
public sealed class ChoreographyE2ETests
{
    [Fact]
    public async Task Router_DispatchesEvent_ToAllRegisteredHandlers()
    {
        var sp = BuildHappyPath();
        var router = sp.GetRequiredService<IChoreographyRouter>();
        var log = sp.GetRequiredService<HandlerInvocationLog>();

        var paymentEvent = new PaymentCaptured(OrderId: "order-001", Amount: 150.00m);
        var result = await router.DispatchAsync(paymentEvent);

        result.IsSuccess.Should().BeTrue();
        log.Invocations.Should().Contain(i => i.Handler == nameof(ShipmentChoreography) && i.Event == nameof(PaymentCaptured));
        log.Invocations.Should().Contain(i => i.Handler == nameof(InventoryChoreography) && i.Event == nameof(PaymentCaptured));
    }

    [Fact]
    public async Task Router_FanoutInProcess_PublishesNextEvent()
    {
        var sp = BuildHappyPath();
        var router = sp.GetRequiredService<IChoreographyRouter>();
        var publisher = (InMemoryIntegrationEventPublisher)sp.GetRequiredService<IIntegrationEventPublisher>();
        var log = sp.GetRequiredService<HandlerInvocationLog>();

        await router.DispatchAsync(new PaymentCaptured(OrderId: "order-002", Amount: 50m));

        publisher.Published.Should().Contain(e => e is PrepareShipmentRequested);
        log.Invocations.Select(i => i.Event).Should().Contain(nameof(PrepareShipmentRequested));
    }

    [Fact]
    public async Task Router_CompensationFlow_PublishesCompensationEvent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerInvocationLog>();
        services.AddSingleton<IChoreographyRouter, ChoreographyRouter>();
        services.AddSingleton<IIntegrationEventPublisher>(sp =>
            new InMemoryIntegrationEventPublisher(() => sp.GetService<IChoreographyRouter>()));
        services.AddTransient<IHandle<PaymentRefunded>, RefundShipmentChoreography>();
        var sp = services.BuildServiceProvider();

        var router = sp.GetRequiredService<IChoreographyRouter>();
        var publisher = (InMemoryIntegrationEventPublisher)sp.GetRequiredService<IIntegrationEventPublisher>();

        await router.DispatchAsync(new PaymentRefunded(OrderId: "order-003"));

        publisher.Published.Should().Contain(e => e is ShipmentCancelled);
    }

    private static IServiceProvider BuildHappyPath()
    {
        // Register handlers explicitly to avoid the assembly scanner picking up
        // intentionally-throwing test fixtures defined elsewhere in this file.
        var services = new ServiceCollection();
        services.AddSingleton<HandlerInvocationLog>();
        services.AddSingleton<IChoreographyRouter, ChoreographyRouter>();
        services.AddSingleton<IIntegrationEventPublisher>(sp =>
            new InMemoryIntegrationEventPublisher(() => sp.GetService<IChoreographyRouter>()));
        services.AddTransient<IHandle<PaymentCaptured>, ShipmentChoreography>();
        services.AddTransient<IHandle<PrepareShipmentRequested>, ShipmentChoreography>();
        services.AddTransient<IHandle<PaymentCaptured>, InventoryChoreography>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Router_HandlerThrows_ErrorIsAggregated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChoreographyRouter, ChoreographyRouter>();
        services.AddSingleton<IIntegrationEventPublisher>(_ => new InMemoryIntegrationEventPublisher());
        services.AddTransient<IHandle<PaymentCaptured>, ThrowingHandler>();
        var sp = services.BuildServiceProvider();

        var router = sp.GetRequiredService<IChoreographyRouter>();
        var result = await router.DispatchAsync(new PaymentCaptured(OrderId: "x", Amount: 1m));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Choreography.HandlerFailures");
        result.Error.Message.Should().Contain("threw");
    }

    [Fact]
    public async Task Router_NoHandlersRegistered_SucceedsWithoutEffect()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChoreographyRouter, ChoreographyRouter>();
        services.AddSingleton<IIntegrationEventPublisher>(_ => new InMemoryIntegrationEventPublisher());
        var sp = services.BuildServiceProvider();

        var router = sp.GetRequiredService<IChoreographyRouter>();
        var result = await router.DispatchAsync(new PaymentCaptured(OrderId: "no-handlers", Amount: 1m));

        result.IsSuccess.Should().BeTrue();
    }

    private sealed class HandlerInvocationLog
    {
        private readonly object _lock = new();

        private readonly List<(string Handler, string Event)> _invocations = new();

        public IReadOnlyList<(string Handler, string Event)> Invocations
        {
            get { lock (_lock) { return _invocations.ToList(); } }
        }

        public void Record(string handler, string @event)
        {
            lock (_lock)
            {
                _invocations.Add((handler, @event));
            }
        }
    }

    private sealed class ShipmentChoreography :
        IHandle<PaymentCaptured>,
        IHandle<PrepareShipmentRequested>
    {
        private readonly HandlerInvocationLog _log;

        public ShipmentChoreography(HandlerInvocationLog log) => _log = log;

        public Task<Result> HandleAsync(PaymentCaptured @event, IChoreographyContext context, CancellationToken cancellationToken = default)
        {
            _log.Record(nameof(ShipmentChoreography), nameof(PaymentCaptured));
            return context.PublishAsync(new PrepareShipmentRequested(@event.OrderId), cancellationToken);
        }

        public Task<Result> HandleAsync(PrepareShipmentRequested @event, IChoreographyContext context, CancellationToken cancellationToken = default)
        {
            _log.Record(nameof(ShipmentChoreography), nameof(PrepareShipmentRequested));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class InventoryChoreography : IHandle<PaymentCaptured>
    {
        private readonly HandlerInvocationLog _log;

        public InventoryChoreography(HandlerInvocationLog log) => _log = log;

        public Task<Result> HandleAsync(PaymentCaptured @event, IChoreographyContext context, CancellationToken cancellationToken = default)
        {
            _log.Record(nameof(InventoryChoreography), nameof(PaymentCaptured));
            return Task.FromResult(Result.Success());
        }
    }

    [Compensation(typeof(PaymentCaptured))]
    private sealed class RefundShipmentChoreography : IHandle<PaymentRefunded>
    {
        public Task<Result> HandleAsync(PaymentRefunded @event, IChoreographyContext context, CancellationToken cancellationToken = default)
        {
            return context.PublishCompensationAsync(new ShipmentCancelled(@event.OrderId), cancellationToken);
        }
    }

    private sealed class ThrowingHandler : IHandle<PaymentCaptured>
    {
        public Task<Result> HandleAsync(PaymentCaptured @event, IChoreographyContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated handler failure");
    }
}

internal sealed record PaymentCaptured(string OrderId, decimal Amount) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;

    public string EventType => nameof(PaymentCaptured);

    public int EventVersion => 1;

    public string? CorrelationId { get; init; }

    public string? CausationId { get; init; }
}

internal sealed record PaymentRefunded(string OrderId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;

    public string EventType => nameof(PaymentRefunded);

    public int EventVersion => 1;

    public string? CorrelationId { get; init; }

    public string? CausationId { get; init; }
}

internal sealed record PrepareShipmentRequested(string OrderId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;

    public string EventType => nameof(PrepareShipmentRequested);

    public int EventVersion => 1;

    public string? CorrelationId { get; init; }

    public string? CausationId { get; init; }
}

internal sealed record ShipmentCancelled(string OrderId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;

    public string EventType => nameof(ShipmentCancelled);

    public int EventVersion => 1;

    public string? CorrelationId { get; init; }

    public string? CausationId { get; init; }
}
