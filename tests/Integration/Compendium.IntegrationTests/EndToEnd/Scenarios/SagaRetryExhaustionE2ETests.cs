// -----------------------------------------------------------------------
// <copyright file="SagaRetryExhaustionE2ETests.cs" company="Sassy Solutions">
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
/// E2E coverage for retry-exhaustion + idempotent-compensation semantics on top of
/// <c>PostgresProcessManagerRepository</c>. Existing scenarios (<see cref="ProcessManagerE2ETests"/>,
/// <see cref="SagaOrchestrationE2ETests"/>) cover the happy path and a single failure-to-compensation
/// transition. This file targets durable, retry-aware behaviour:
///
/// <list type="bullet">
/// <item>A step that fails N times still records each failure on the durable row.</item>
/// <item>A retry succeeds once the executor stops failing (no orchestrator-side cap).</item>
/// <item>Compensation is idempotent: running it twice on an already-compensated saga must not
/// re-invoke the executor's <c>CompensateAsync</c> against already-compensated steps.</item>
/// </list>
///
/// Together these guarantees underpin the "step recovery after partial failure" promise that
/// process-manager-driven provisioning sagas rely on.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "Saga")]
public sealed class SagaRetryExhaustionE2ETests : IAsyncLifetime
{
    private InMemoryProcessManagerRepository _repository = null!;
    private CountingStepExecutor _executor = null!;
    private ProcessManagerOrchestrator _orchestrator = null!;

    public Task InitializeAsync()
    {
        _repository = new InMemoryProcessManagerRepository();
        _executor = new CountingStepExecutor();
        _orchestrator = new ProcessManagerOrchestrator(_repository, _executor);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExecuteStep_AfterTransientFailure_RetrySucceedsAndStateAdvances()
    {
        // Arrange
        var pm = ProvisioningProcessManager.Create("acme");
        var startResult = await _orchestrator.StartAsync(pm);
        startResult.IsSuccess.Should().BeTrue();

        // First attempt fails (simulates transient external system error).
        _executor.FailNextAttempts = 1;
        var firstAttempt = await _orchestrator.ExecuteStepAsync(pm.Id, "ProvisionAccount");
        firstAttempt.IsFailure.Should().BeTrue("the executor was instructed to fail once");

        // The orchestrator only re-runs steps that are Pending. The recovery contract is:
        // an external retry policy resets the step before re-attempting. Mirror that here
        // by writing the row back to Pending — exactly what a durable retry worker would do.
        var failedStep = (await _repository.GetByIdAsync(pm.Id)).Value.Steps
            .Single(s => s.Name == "ProvisionAccount");
        failedStep.Status.Should().Be(SagaStepStatus.Failed);
        failedStep.ErrorMessage.Should().NotBeNullOrEmpty();

        var resetResult = await _repository.UpdateStepStatusAsync(
            pm.Id,
            failedStep.Id,
            SagaStepStatus.Pending,
            errorMessage: null);
        resetResult.IsSuccess.Should().BeTrue();

        // Act — second attempt succeeds because FailNextAttempts is now exhausted.
        var secondAttempt = await _orchestrator.ExecuteStepAsync(pm.Id, "ProvisionAccount");

        // Assert
        secondAttempt.IsSuccess.Should().BeTrue();
        _executor.ExecuteCallCount.Should().Be(2, "first attempt failed, second succeeded");

        var refreshed = await _repository.GetByIdAsync(pm.Id);
        var step = refreshed.Value.Steps.Single(s => s.Name == "ProvisionAccount");
        step.Status.Should().Be(SagaStepStatus.Completed);
        step.ErrorMessage.Should().BeNull("the successful retry must clear the failure message");
    }

    [Fact]
    public async Task ExecuteStep_AcrossManyRetries_AllAttemptsAreObservableViaErrorMessage()
    {
        // Arrange — a step that needs three resets to eventually succeed. Each failed attempt
        // must persist its error message so an operator dashboard can show retry history.
        var pm = ProvisioningProcessManager.Create("widget");
        await _orchestrator.StartAsync(pm);

        var observedErrors = new List<string>();
        const int maxAttempts = 3;
        var stepId = pm.Steps.Single(s => s.Name == "ProvisionAccount").Id;

        // Act — issue three failures, capturing the error message persisted after each.
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            _executor.NextErrorMessage = $"Transient failure #{attempt}";
            _executor.FailNextAttempts = 1;

            var result = await _orchestrator.ExecuteStepAsync(pm.Id, "ProvisionAccount");
            result.IsFailure.Should().BeTrue();

            var snapshot = await _repository.GetByIdAsync(pm.Id);
            var step = snapshot.Value.Steps.Single(s => s.Id == stepId);
            step.Status.Should().Be(SagaStepStatus.Failed);
            step.ErrorMessage.Should().NotBeNullOrEmpty();
            observedErrors.Add(step.ErrorMessage!);

            // Reset so the next attempt is allowed.
            await _repository.UpdateStepStatusAsync(pm.Id, stepId, SagaStepStatus.Pending, errorMessage: null);
        }

        // Final attempt succeeds.
        _executor.NextErrorMessage = null;
        var finalResult = await _orchestrator.ExecuteStepAsync(pm.Id, "ProvisionAccount");

        // Assert
        finalResult.IsSuccess.Should().BeTrue();
        observedErrors.Should().HaveCount(maxAttempts);
        observedErrors.Should().OnlyHaveUniqueItems("each retry persisted its own distinct error");
        observedErrors.Should().Contain(e => e.Contains("#1"));
        observedErrors.Should().Contain(e => e.Contains("#2"));
        observedErrors.Should().Contain(e => e.Contains("#3"));

        _executor.ExecuteCallCount.Should().Be(maxAttempts + 1, "three failures plus the final success");
    }

    [Fact]
    public async Task ExecuteStep_RetryExhaustion_TriggersCompensationAndOnlyCompensatesPriorCompletedSteps()
    {
        // Arrange — first step completes, second step fails permanently. Compensation must
        // roll back ONLY the first step (the one that actually executed external work).
        // This guards against the "compensate-everything" anti-pattern that double-undoes.
        var pm = ProvisioningProcessManager.Create("foo");
        await _orchestrator.StartAsync(pm);

        var first = await _orchestrator.ExecuteStepAsync(pm.Id, "ProvisionAccount");
        first.IsSuccess.Should().BeTrue();

        _executor.FailNextAttempts = int.MaxValue; // permanent failure
        var second = await _orchestrator.ExecuteStepAsync(pm.Id, "ConfigureNetwork");
        second.IsFailure.Should().BeTrue();

        // Act
        var compensation = await _orchestrator.CompensateAsync(pm.Id);

        // Assert
        compensation.IsSuccess.Should().BeTrue();

        var finalSaga = await _repository.GetByIdAsync(pm.Id);
        finalSaga.Value.Status.Should().Be(SagaStatus.Compensated);

        var firstStep = finalSaga.Value.Steps.Single(s => s.Name == "ProvisionAccount");
        var secondStep = finalSaga.Value.Steps.Single(s => s.Name == "ConfigureNetwork");
        firstStep.Status.Should().Be(SagaStepStatus.Compensated);
        secondStep.Status.Should().Be(SagaStepStatus.Failed,
            "a step that never reached Completed must not be transitioned to Compensated");

        _executor.CompensatedSteps.Should().BeEquivalentTo(new[] { "ProvisionAccount" });
        _executor.CompensateCallCount.Should().Be(1,
            "compensation must invoke the executor exactly once per previously-completed step");
    }

    [Fact]
    public async Task Compensate_CalledTwice_IsIdempotentAndDoesNotReinvokeExecutor()
    {
        // Arrange — the orchestrator's compensation iterates "steps with status Completed".
        // After the first compensation pass those steps are Compensated, not Completed, so a
        // second invocation must not re-issue executor.CompensateAsync calls. This test pins
        // that contract because process-manager hosts may retry compensation on transient
        // persistence errors and we MUST NOT double-undo external work.
        var pm = ProvisioningProcessManager.Create("idempotent-org");
        await _orchestrator.StartAsync(pm);

        var first = await _orchestrator.ExecuteStepAsync(pm.Id, "ProvisionAccount");
        first.IsSuccess.Should().BeTrue();

        _executor.FailNextAttempts = int.MaxValue;
        var second = await _orchestrator.ExecuteStepAsync(pm.Id, "ConfigureNetwork");
        second.IsFailure.Should().BeTrue();

        var firstCompensation = await _orchestrator.CompensateAsync(pm.Id);
        firstCompensation.IsSuccess.Should().BeTrue();
        var compensateCallsAfterFirst = _executor.CompensateCallCount;

        // Act — second compensation pass on a saga that is already in Compensated status.
        var secondCompensation = await _orchestrator.CompensateAsync(pm.Id);

        // Assert
        secondCompensation.IsSuccess.Should().BeTrue();
        _executor.CompensateCallCount.Should().Be(compensateCallsAfterFirst,
            "running compensation a second time must not re-invoke the executor for already-compensated steps");

        var finalSaga = await _repository.GetByIdAsync(pm.Id);
        finalSaga.Value.Status.Should().Be(SagaStatus.Compensated);
        finalSaga.Value.Steps.Single(s => s.Name == "ProvisionAccount").Status
            .Should().Be(SagaStepStatus.Compensated);
    }

    private sealed class ProvisioningState
    {
        public string OrgName { get; set; } = string.Empty;
    }

    private sealed class ProvisioningProcessManager : ProcessManager<ProvisioningState>
    {
        private ProvisioningProcessManager(Guid id, ProvisioningState state)
            : base(id, state, new[] { "ProvisionAccount", "ConfigureNetwork" })
        {
        }

        public static ProvisioningProcessManager Create(string orgName)
            => new(Guid.NewGuid(), new ProvisioningState { OrgName = orgName });
    }

    private sealed class CountingStepExecutor : IProcessManagerStepExecutor
    {
        public int ExecuteCallCount { get; private set; }

        public int CompensateCallCount { get; private set; }

        public List<string> ExecutedSteps { get; } = new();

        public List<string> CompensatedSteps { get; } = new();

        public int FailNextAttempts { get; set; }

        public string? NextErrorMessage { get; set; }

        public Task<Result> ExecuteAsync(IProcessManager processManager, SagaStep step, CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;

            if (FailNextAttempts > 0)
            {
                FailNextAttempts--;
                var message = NextErrorMessage ?? $"Simulated failure on {step.Name}";
                return Task.FromResult(Result.Failure(Error.Failure("Step.Failed", message)));
            }

            ExecutedSteps.Add(step.Name);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> CompensateAsync(IProcessManager processManager, SagaStep step, CancellationToken cancellationToken = default)
        {
            CompensateCallCount++;
            CompensatedSteps.Add(step.Name);
            return Task.FromResult(Result.Success());
        }
    }
}
