// -----------------------------------------------------------------------
// <copyright file="ProcessManagerE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Sagas.Common;
using Compendium.Abstractions.Sagas.ProcessManagers;
using Compendium.Application.Sagas.ProcessManagers;
using Compendium.Core.Results;
using AwesomeAssertions;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E coverage for the new orchestration-saga ("Process Manager") API. Mirrors the
/// legacy <c>SagaOrchestrationE2ETests</c> scenarios with the deprecated <c>ISaga</c>/
/// <c>SagaOrchestrator</c> types replaced by <c>IProcessManager</c>/<c>ProcessManagerOrchestrator</c>.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Saga")]
public sealed class ProcessManagerE2ETests
{
    [Fact]
    public async Task Orchestrator_HappyPath_AllStepsComplete()
    {
        var (orchestrator, repository, executor) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-001", 150.00m);
        await repository.SaveAsync(pm);

        var step1 = await orchestrator.ExecuteStepAsync(pm.Id, "ReserveInventory");
        var step2 = await orchestrator.ExecuteStepAsync(pm.Id, "ProcessPayment");
        var step3 = await orchestrator.ExecuteStepAsync(pm.Id, "ShipOrder");

        step1.IsSuccess.Should().BeTrue();
        step2.IsSuccess.Should().BeTrue();
        step3.IsSuccess.Should().BeTrue();

        var loaded = await orchestrator.GetAsync(pm.Id);
        loaded.IsSuccess.Should().BeTrue();
        loaded.Value.Status.Should().Be(SagaStatus.Completed);
        loaded.Value.Steps.Should().OnlyContain(s => s.Status == SagaStepStatus.Completed);
        executor.ExecutedSteps.Should().BeEquivalentTo(new[] { "ReserveInventory", "ProcessPayment", "ShipOrder" });
        executor.CompensatedSteps.Should().BeEmpty();
    }

    [Fact]
    public async Task Orchestrator_StepFails_StatusMarkedFailedAndCompensationRollsBack()
    {
        var (orchestrator, repository, executor) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-002", 200.00m);
        await repository.SaveAsync(pm);

        await orchestrator.ExecuteStepAsync(pm.Id, "ReserveInventory");

        executor.FailNext = true;
        var paymentResult = await orchestrator.ExecuteStepAsync(pm.Id, "ProcessPayment");
        paymentResult.IsFailure.Should().BeTrue();

        var afterFailure = await orchestrator.GetAsync(pm.Id);
        afterFailure.Value.Steps.First(s => s.Name == "ProcessPayment").Status
            .Should().Be(SagaStepStatus.Failed);
        afterFailure.Value.Steps.First(s => s.Name == "ProcessPayment").ErrorMessage
            .Should().NotBeNullOrWhiteSpace();

        var compensation = await orchestrator.CompensateAsync(pm.Id);
        compensation.IsSuccess.Should().BeTrue();

        var afterCompensation = await orchestrator.GetAsync(pm.Id);
        afterCompensation.Value.Status.Should().Be(SagaStatus.Compensated);
        afterCompensation.Value.Steps.First(s => s.Name == "ReserveInventory").Status
            .Should().Be(SagaStepStatus.Compensated);

        executor.CompensatedSteps.Should().Contain("ReserveInventory");
    }

    [Fact]
    public async Task Orchestrator_StepNotPending_ReturnsConflict()
    {
        var (orchestrator, repository, _) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-003", 50.00m);
        await repository.SaveAsync(pm);

        await orchestrator.ExecuteStepAsync(pm.Id, "ReserveInventory");
        var second = await orchestrator.ExecuteStepAsync(pm.Id, "ReserveInventory");

        second.IsFailure.Should().BeTrue();
        second.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Orchestrator_UnknownStep_ReturnsNotFound()
    {
        var (orchestrator, repository, _) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-004", 75.00m);
        await repository.SaveAsync(pm);

        var result = await orchestrator.ExecuteStepAsync(pm.Id, "DoesNotExist");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Repository_TypedGetByIdAsync_ReturnsSameInstanceWithTypedState()
    {
        var (_, repository, _) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-typed", 99.99m);
        await repository.SaveAsync(pm);

        var typed = await repository.GetByIdAsync<OrderProcessState>(pm.Id);

        typed.IsSuccess.Should().BeTrue();
        typed.Value.Id.Should().Be(pm.Id);
        typed.Value.State.OrderId.Should().Be("order-typed");
        typed.Value.State.Amount.Should().Be(99.99m);
    }

    [Fact]
    public async Task Repository_TypedGetByIdAsync_StateTypeMismatch_ReturnsConflict()
    {
        var (_, repository, _) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-mismatch", 1.0m);
        await repository.SaveAsync(pm);

        var wrongType = await repository.GetByIdAsync<UnrelatedState>(pm.Id);

        wrongType.IsFailure.Should().BeTrue();
        wrongType.Error.Type.Should().Be(ErrorType.Conflict);
        wrongType.Error.Code.Should().Be("ProcessManager.StateTypeMismatch");
    }

    [Fact]
    public async Task Repository_TypedGetByIdAsync_NotFound_ReturnsNotFound()
    {
        var (_, repository, _) = BuildOrchestrator();

        var loaded = await repository.GetByIdAsync<OrderProcessState>(Guid.NewGuid());

        loaded.IsFailure.Should().BeTrue();
        loaded.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Orchestrator_MultipleStepsInSequence_TimestampsAreNonDecreasing()
    {
        // Note: assertion uses BeOnOrBefore rather than BeBefore because system-clock
        // resolution on Windows can be ~15ms, which is coarser than typical step latency.
        var (orchestrator, repository, _) = BuildOrchestrator();
        var pm = OrderProcessManager.Create("order-005", 300.00m);
        await repository.SaveAsync(pm);

        await orchestrator.ExecuteStepAsync(pm.Id, "ReserveInventory");
        await orchestrator.ExecuteStepAsync(pm.Id, "ProcessPayment");
        await orchestrator.ExecuteStepAsync(pm.Id, "ShipOrder");

        var final = await orchestrator.GetAsync(pm.Id);
        var ordered = final.Value.Steps.OrderBy(s => s.Order).ToList();

        ordered.Should().HaveCount(3);
        ordered.Should().OnlyContain(s => s.ExecutedAt.HasValue);
        ordered[0].ExecutedAt!.Value.Should().BeOnOrBefore(ordered[1].ExecutedAt!.Value);
        ordered[1].ExecutedAt!.Value.Should().BeOnOrBefore(ordered[2].ExecutedAt!.Value);
    }

    private static (
        IProcessManagerOrchestrator orchestrator,
        InMemoryProcessManagerRepository repository,
        TestStepExecutor executor) BuildOrchestrator()
    {
        var repository = new InMemoryProcessManagerRepository();
        var executor = new TestStepExecutor();
        var orchestrator = new ProcessManagerOrchestrator(repository, executor);
        return (orchestrator, repository, executor);
    }

    private sealed class OrderProcessState
    {
        public string OrderId { get; set; } = string.Empty;

        public decimal Amount { get; set; }
    }

    private sealed class UnrelatedState
    {
        public string Anything { get; set; } = string.Empty;
    }

    private sealed class OrderProcessManager : ProcessManager<OrderProcessState>
    {
        private OrderProcessManager(Guid id, OrderProcessState state)
            : base(id, state, new[] { "ReserveInventory", "ProcessPayment", "ShipOrder" })
        {
        }

        public static OrderProcessManager Create(string orderId, decimal amount)
            => new(Guid.NewGuid(), new OrderProcessState { OrderId = orderId, Amount = amount });
    }

    private sealed class TestStepExecutor : IProcessManagerStepExecutor
    {
        public List<string> ExecutedSteps { get; } = new();

        public List<string> CompensatedSteps { get; } = new();

        public bool FailNext { get; set; }

        public Task<Result> ExecuteAsync(IProcessManager processManager, SagaStep step, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                return Task.FromResult(Result.Failure(Error.Failure("Step.Failed", $"{step.Name} failed (simulated).")));
            }

            ExecutedSteps.Add(step.Name);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> CompensateAsync(IProcessManager processManager, SagaStep step, CancellationToken cancellationToken = default)
        {
            CompensatedSteps.Add(step.Name);
            return Task.FromResult(Result.Success());
        }
    }
}
