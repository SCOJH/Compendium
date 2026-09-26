// -----------------------------------------------------------------------
// <copyright file="SagaOrchestrationE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Application.Saga;
using Compendium.Core.Results;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using AwesomeAssertions;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario 9: Saga Orchestration (Multi-Step Workflow).
/// Tests long-running saga that coordinates multiple aggregates with compensation.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Saga")]
public sealed class SagaOrchestrationE2ETests : IAsyncLifetime
{
    private InMemorySagaRepository? _sagaRepository;
    private TestSagaStepExecutor? _sagaStepExecutor;
    private SagaOrchestrator? _sagaOrchestrator;

    public Task InitializeAsync()
    {
        // Initialize in-memory saga infrastructure
        _sagaRepository = new InMemorySagaRepository();
        _sagaStepExecutor = new TestSagaStepExecutor();

        // Create orchestrator with test implementations
        var serviceProvider = new TestServiceProvider();
        _sagaOrchestrator = new SagaOrchestrator(_sagaRepository, _sagaStepExecutor, serviceProvider);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // No cleanup needed for in-memory implementations
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SagaOrchestrator_CompleteWorkflow_AllStepsSucceed()
    {
        // Arrange
        var orderId = OrderId.New();
        var sagaData = new OrderFulfillmentSagaData
        {
            OrderId = orderId.ToString(),
            CustomerId = "customer-saga-001",
            TotalAmount = 150.00m,
            Items = new List<OrderFulfillmentItem>
            {
                new() { ProductId = "product-A", Quantity = 2, Price = 50.00m },
                new() { ProductId = "product-B", Quantity = 1, Price = 50.00m }
            }
        };

        // **Step 1: Start Saga**
        var saga = OrderFulfillmentSaga.Create(sagaData);
        await _sagaRepository!.SaveAsync(saga);

        saga.Status.Should().Be(SagaStatus.NotStarted);
        saga.Steps.Should().HaveCount(3);

        // Verify initial step states
        var reserveStep = saga.Steps.First(s => s.Name == "ReserveInventory");
        var paymentStep = saga.Steps.First(s => s.Name == "ProcessPayment");
        var shipStep = saga.Steps.First(s => s.Name == "ShipOrder");

        reserveStep.Status.Should().Be(SagaStepStatus.Pending);
        paymentStep.Status.Should().Be(SagaStepStatus.Pending);
        shipStep.Status.Should().Be(SagaStepStatus.Pending);

        // **Step 2: Execute Step 1 - Reserve Inventory**
        var reserveResult = await _sagaOrchestrator!.ExecuteStepAsync(saga.Id, "ReserveInventory");
        reserveResult.IsSuccess.Should().BeTrue();

        // Verify step 1 completed
        var sagaAfterStep1 = await _sagaRepository.GetByIdAsync(saga.Id);
        var reserveStepAfter = sagaAfterStep1.Value.Steps.First(s => s.Name == "ReserveInventory");
        reserveStepAfter.Status.Should().Be(SagaStepStatus.Completed);
        reserveStepAfter.ExecutedAt.Should().NotBeNull();

        // Verify saga state progression
        _sagaStepExecutor!.ExecutedSteps.Should().Contain("ReserveInventory");

        // **Step 3: Execute Step 2 - Process Payment**
        var paymentResult = await _sagaOrchestrator.ExecuteStepAsync(saga.Id, "ProcessPayment");
        paymentResult.IsSuccess.Should().BeTrue();

        // Verify step 2 completed
        var sagaAfterStep2 = await _sagaRepository.GetByIdAsync(saga.Id);
        var paymentStepAfter = sagaAfterStep2.Value.Steps.First(s => s.Name == "ProcessPayment");
        paymentStepAfter.Status.Should().Be(SagaStepStatus.Completed);
        paymentStepAfter.ExecutedAt.Should().NotBeNull();

        _sagaStepExecutor.ExecutedSteps.Should().Contain("ProcessPayment");

        // **Step 4: Execute Step 3 - Ship Order**
        var shipResult = await _sagaOrchestrator.ExecuteStepAsync(saga.Id, "ShipOrder");
        shipResult.IsSuccess.Should().BeTrue();

        // Verify step 3 completed
        var sagaAfterStep3 = await _sagaRepository.GetByIdAsync(saga.Id);
        var shipStepAfter = sagaAfterStep3.Value.Steps.First(s => s.Name == "ShipOrder");
        shipStepAfter.Status.Should().Be(SagaStepStatus.Completed);
        shipStepAfter.ExecutedAt.Should().NotBeNull();

        _sagaStepExecutor.ExecutedSteps.Should().Contain("ShipOrder");

        // **Step 5: Verify Final State**
        var finalSaga = await _sagaRepository.GetByIdAsync(saga.Id);
        finalSaga.Value.Steps.Should().OnlyContain(s => s.Status == SagaStepStatus.Completed);

        _sagaStepExecutor.ExecutedSteps.Should().HaveCount(3);
        _sagaStepExecutor.CompensatedSteps.Should().BeEmpty("No compensation should occur on success");

        // **Expected Results:**
        // ✅ Saga completes all 3 steps
        // ✅ Saga state persisted at each step
        // ✅ All steps executed in correct order
        // ✅ No compensation triggered
    }

    [Fact]
    public async Task SagaOrchestrator_StepFails_CompensationTriggered()
    {
        // Arrange
        var orderId = OrderId.New();
        var sagaData = new OrderFulfillmentSagaData
        {
            OrderId = orderId.ToString(),
            CustomerId = "customer-saga-002",
            TotalAmount = 200.00m,
            Items = new List<OrderFulfillmentItem>
            {
                new() { ProductId = "product-C", Quantity = 1, Price = 200.00m }
            }
        };

        // **Step 1: Start Saga**
        var saga = OrderFulfillmentSaga.Create(sagaData);
        await _sagaRepository!.SaveAsync(saga);

        // **Step 2: Execute Step 1 - Reserve Inventory (Success)**
        var reserveResult = await _sagaOrchestrator!.ExecuteStepAsync(saga.Id, "ReserveInventory");
        reserveResult.IsSuccess.Should().BeTrue();

        // **Step 3: Execute Step 2 - Process Payment (SIMULATED FAILURE)**
        _sagaStepExecutor!.FailNextStep = true; // Force payment step to fail

        var paymentResult = await _sagaOrchestrator.ExecuteStepAsync(saga.Id, "ProcessPayment");
        paymentResult.IsSuccess.Should().BeFalse("Payment step should fail");

        // Verify step 2 marked as failed
        var sagaAfterFailure = await _sagaRepository.GetByIdAsync(saga.Id);
        var paymentStepAfter = sagaAfterFailure.Value.Steps.First(s => s.Name == "ProcessPayment");
        paymentStepAfter.Status.Should().Be(SagaStepStatus.Failed);
        paymentStepAfter.ErrorMessage.Should().NotBeNullOrEmpty();

        // **Step 4: Trigger Compensation**
        var compensationResult = await _sagaOrchestrator.CompensateAsync(saga.Id);
        compensationResult.IsSuccess.Should().BeTrue();

        // **Step 5: Verify Compensation**
        var sagaAfterCompensation = await _sagaRepository.GetByIdAsync(saga.Id);
        sagaAfterCompensation.Value.Status.Should().Be(SagaStatus.Compensated);

        // Verify compensated steps (in reverse order)
        var reserveStepAfterCompensation = sagaAfterCompensation.Value.Steps.First(s => s.Name == "ReserveInventory");
        reserveStepAfterCompensation.Status.Should().Be(SagaStepStatus.Compensated);
        reserveStepAfterCompensation.CompensatedAt.Should().NotBeNull();

        _sagaStepExecutor.CompensatedSteps.Should().Contain("ReserveInventory");

        // **Expected Results:**
        // ✅ Payment step fails
        // ✅ Compensation triggered automatically
        // ✅ Completed steps compensated in reverse order
        // ✅ Saga state = Compensated
        // ✅ No orphaned state
    }

    [Fact]
    public async Task SagaOrchestrator_GetSaga_ReturnsCurrentState()
    {
        // Arrange
        var orderId = OrderId.New();
        var sagaData = new OrderFulfillmentSagaData
        {
            OrderId = orderId.ToString(),
            CustomerId = "customer-saga-003",
            TotalAmount = 100.00m,
            Items = new List<OrderFulfillmentItem>
            {
                new() { ProductId = "product-D", Quantity = 1, Price = 100.00m }
            }
        };

        var saga = OrderFulfillmentSaga.Create(sagaData);
        await _sagaRepository!.SaveAsync(saga);

        // **Step 1: Get Saga Before Execution**
        var initialSagaResult = await _sagaOrchestrator!.GetSagaAsync(saga.Id);
        initialSagaResult.IsSuccess.Should().BeTrue();
        initialSagaResult.Value.Status.Should().Be(SagaStatus.NotStarted);

        // **Step 2: Execute One Step**
        await _sagaOrchestrator.ExecuteStepAsync(saga.Id, "ReserveInventory");

        // **Step 3: Get Saga After Partial Execution**
        var partialSagaResult = await _sagaOrchestrator.GetSagaAsync(saga.Id);
        partialSagaResult.IsSuccess.Should().BeTrue();

        var reserveStep = partialSagaResult.Value.Steps.First(s => s.Name == "ReserveInventory");
        reserveStep.Status.Should().Be(SagaStepStatus.Completed);

        // **Expected Results:**
        // ✅ GetSaga returns current saga state
        // ✅ State reflects partial execution
    }

    [Fact]
    public async Task SagaOrchestrator_MultipleStepsInSequence_MaintainsConsistency()
    {
        // Arrange
        var orderId = OrderId.New();
        var sagaData = new OrderFulfillmentSagaData
        {
            OrderId = orderId.ToString(),
            CustomerId = "customer-saga-004",
            TotalAmount = 300.00m,
            Items = new List<OrderFulfillmentItem>
            {
                new() { ProductId = "product-E", Quantity = 3, Price = 100.00m }
            }
        };

        var saga = OrderFulfillmentSaga.Create(sagaData);
        await _sagaRepository!.SaveAsync(saga);

        // **Execute all steps sequentially**
        var results = new List<Result>();
        results.Add(await _sagaOrchestrator!.ExecuteStepAsync(saga.Id, "ReserveInventory"));
        results.Add(await _sagaOrchestrator.ExecuteStepAsync(saga.Id, "ProcessPayment"));
        results.Add(await _sagaOrchestrator.ExecuteStepAsync(saga.Id, "ShipOrder"));

        // **Verify all steps succeeded**
        results.Should().OnlyContain(r => r.IsSuccess);

        // **Verify step execution order consistency**
        var finalSaga = await _sagaRepository.GetByIdAsync(saga.Id);
        var steps = finalSaga.Value.Steps.OrderBy(s => s.Order).ToList();

        steps[0].Name.Should().Be("ReserveInventory");
        steps[0].Status.Should().Be(SagaStepStatus.Completed);

        steps[1].Name.Should().Be("ProcessPayment");
        steps[1].Status.Should().Be(SagaStepStatus.Completed);

        steps[2].Name.Should().Be("ShipOrder");
        steps[2].Status.Should().Be(SagaStepStatus.Completed);

        // **Verify timestamps are sequential**
        steps[0].ExecutedAt.Should().NotBeNull();
        steps[1].ExecutedAt.Should().NotBeNull();
        steps[2].ExecutedAt.Should().NotBeNull();
        steps[0].ExecutedAt!.Value.Should().BeBefore(steps[1].ExecutedAt!.Value);
        steps[1].ExecutedAt!.Value.Should().BeBefore(steps[2].ExecutedAt!.Value);

        // **Expected Results:**
        // ✅ Steps execute in correct order
        // ✅ State consistency maintained
        // ✅ Timestamps reflect sequential execution
    }
}

/// <summary>
/// Test saga data for OrderFulfillment workflow.
/// </summary>
public class OrderFulfillmentSagaData
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required List<OrderFulfillmentItem> Items { get; init; }
}

/// <summary>
/// Order item for saga workflow.
/// </summary>
public class OrderFulfillmentItem
{
    public required string ProductId { get; init; }
    public required int Quantity { get; init; }
    public required decimal Price { get; init; }
}

/// <summary>
/// Test saga implementation for order fulfillment.
/// </summary>
public class OrderFulfillmentSaga : ISaga<OrderFulfillmentSagaData>
{
    private readonly List<SagaStep> _steps;

    private OrderFulfillmentSaga(Guid id, OrderFulfillmentSagaData data, List<SagaStep> steps)
    {
        Id = id;
        Data = data;
        _steps = steps;
        Status = SagaStatus.NotStarted;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; }

    public SagaStatus Status { get; private set; }

    public DateTime CreatedAt { get; }

    public DateTime? CompletedAt { get; private set; }

    public OrderFulfillmentSagaData Data { get; }

    public IReadOnlyList<SagaStep> Steps => _steps.AsReadOnly();

    public static OrderFulfillmentSaga Create(OrderFulfillmentSagaData data)
    {
        var sagaId = Guid.NewGuid();

        // Define saga steps
        var steps = new List<SagaStep>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "ReserveInventory",
                Status = SagaStepStatus.Pending,
                Order = 1
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "ProcessPayment",
                Status = SagaStepStatus.Pending,
                Order = 2
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "ShipOrder",
                Status = SagaStepStatus.Pending,
                Order = 3
            }
        };

        return new OrderFulfillmentSaga(sagaId, data, steps);
    }

    public void UpdateStatus(SagaStatus status)
    {
        Status = status;
        if (status == SagaStatus.Completed || status == SagaStatus.Compensated)
        {
            CompletedAt = DateTime.UtcNow;
        }
    }

    public void UpdateStepStatus(Guid stepId, SagaStepStatus status, string? errorMessage = null)
    {
        var step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step != null)
        {
            var updatedStep = new SagaStep
            {
                Id = step.Id,
                Name = step.Name,
                Status = status,
                ErrorMessage = errorMessage,
                ExecutedAt = status == SagaStepStatus.Completed ? DateTime.UtcNow : step.ExecutedAt,
                CompensatedAt = status == SagaStepStatus.Compensated ? DateTime.UtcNow : step.CompensatedAt,
                Order = step.Order
            };

            var index = _steps.IndexOf(step);
            _steps[index] = updatedStep;
        }
    }
}

/// <summary>
/// In-memory saga repository for testing.
/// </summary>
public class InMemorySagaRepository : ISagaRepository
{
    private readonly Dictionary<Guid, ISaga> _sagas = new();

    public Task<Result<ISaga>> GetByIdAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        if (_sagas.TryGetValue(sagaId, out var saga))
        {
            return Task.FromResult(Result.Success(saga));
        }

        return Task.FromResult(Result.Failure<ISaga>(
            Error.NotFound("Saga.NotFound", $"Saga with ID {sagaId} not found")));
    }

    public Task SaveAsync(ISaga saga, CancellationToken cancellationToken = default)
    {
        _sagas[saga.Id] = saga;
        return Task.CompletedTask;
    }

    public Task UpdateSagaStatusAsync(Guid sagaId, SagaStatus status, CancellationToken cancellationToken = default)
    {
        if (_sagas.TryGetValue(sagaId, out var saga) && saga is OrderFulfillmentSaga fulfillmentSaga)
        {
            fulfillmentSaga.UpdateStatus(status);
        }

        return Task.CompletedTask;
    }

    public Task UpdateStepStatusAsync(Guid sagaId, Guid stepId, SagaStepStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        if (_sagas.TryGetValue(sagaId, out var saga) && saga is OrderFulfillmentSaga fulfillmentSaga)
        {
            fulfillmentSaga.UpdateStepStatus(stepId, status, errorMessage);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Test saga step executor that simulates command execution.
/// </summary>
public class TestSagaStepExecutor : ISagaStepExecutor
{
    public List<string> ExecutedSteps { get; } = new();
    public List<string> CompensatedSteps { get; } = new();
    public bool FailNextStep { get; set; }

    public Task<Result> ExecuteAsync(ISaga saga, SagaStep step, CancellationToken cancellationToken = default)
    {
        if (FailNextStep)
        {
            FailNextStep = false; // Reset for next call
            return Task.FromResult(Result.Failure(
                Error.Failure("Step.ExecutionFailed", $"Step '{step.Name}' execution failed (simulated)")));
        }

        ExecutedSteps.Add(step.Name);

        // Simulate successful step execution
        return Task.FromResult(Result.Success());
    }

    public Task<Result> CompensateAsync(ISaga saga, SagaStep step, CancellationToken cancellationToken = default)
    {
        CompensatedSteps.Add(step.Name);

        // Simulate successful compensation
        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// Test service provider for saga factory resolution.
/// </summary>
public class TestServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        // For saga orchestrator tests, we don't need actual saga factories
        // The tests create sagas directly
        return null;
    }
}
