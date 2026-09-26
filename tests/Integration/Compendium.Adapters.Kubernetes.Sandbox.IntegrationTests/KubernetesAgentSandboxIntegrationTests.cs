// -----------------------------------------------------------------------
// <copyright file="KubernetesAgentSandboxIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.CodingAgents.Sandbox;
using Compendium.Adapters.Kubernetes.Sandbox;
using Compendium.Core.Results;
using AwesomeAssertions;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Compendium.Adapters.Kubernetes.Sandbox.IntegrationTests;

[Collection(K3sClusterCollection.Name)]
public class KubernetesAgentSandboxIntegrationTests
{
    private readonly K3sClusterFixture _fixture;

    public KubernetesAgentSandboxIntegrationTests(K3sClusterFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task EnsureNamespaceAsync(string ns)
    {
        try
        {
            await _fixture.Client.CoreV1.ReadNamespaceAsync(ns);
        }
        catch
        {
            await _fixture.Client.CoreV1.CreateNamespaceAsync(new V1Namespace
            {
                Metadata = new V1ObjectMeta { Name = ns },
            });
        }
    }

    private async Task EnsureServiceAccountAsync(string ns, string name)
    {
        try
        {
            await _fixture.Client.CoreV1.ReadNamespacedServiceAccountAsync(name, ns);
        }
        catch
        {
            await _fixture.Client.CoreV1.CreateNamespacedServiceAccountAsync(new V1ServiceAccount
            {
                Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = ns },
            }, ns);
        }
    }

    private KubernetesAgentSandbox BuildSandbox(KubernetesSandboxOptions opts)
        => new(_fixture.Client, opts, NullLogger<KubernetesAgentSandbox>.Instance);

    [SkippableFact]
    public async Task Sandbox_can_exec_read_write_and_edit_files_end_to_end()
    {
        Skip.IfNot(_fixture.IsAvailable, $"k3s unavailable: {_fixture.UnavailableReason}");

        const string ns = "coding-agents-it";
        const string sa = "coding-agent-sandbox";
        await EnsureNamespaceAsync(ns);
        await EnsureServiceAccountAsync(ns, sa);

        var opts = new KubernetesSandboxOptions
        {
            Image = "busybox:1.36",
            Namespace = ns,
            ServiceAccountName = sa,
            ReadOnlyRootFilesystem = false, // busybox doesn't have a writable ephemeral layer separated out
            PodReadyTimeout = TimeSpan.FromSeconds(120),
        };

        await using var sandbox = BuildSandbox(opts);

        var start = await sandbox.StartAsync(new SandboxOptions
        {
            Kind = SandboxKind.KubernetesPod,
            WorkingDirectory = "/workspace",
        });
        start.IsSuccess.Should().BeTrue(because: start.IsFailure ? start.Error.Message : string.Empty);

        var echo = await sandbox.ExecBashAsync("echo hello-from-sandbox");
        echo.IsSuccess.Should().BeTrue();
        echo.Value.Stdout.Should().Contain("hello-from-sandbox");
        echo.Value.ExitCode.Should().Be(0);

        var write = await sandbox.WriteFileAsync("hello.txt", "the quick brown fox");
        write.IsSuccess.Should().BeTrue(because: write.IsFailure ? write.Error.Message : string.Empty);

        var read = await sandbox.ReadFileAsync("hello.txt");
        read.IsSuccess.Should().BeTrue();
        read.Value.Should().Be("the quick brown fox");

        var edit = await sandbox.EditFileAsync("hello.txt", "quick brown", "slow grey");
        edit.IsSuccess.Should().BeTrue();
        var reread = await sandbox.ReadFileAsync("hello.txt");
        reread.Value.Should().Be("the slow grey fox");
    }

    [SkippableFact]
    public async Task Sandbox_disposes_pod_even_when_caller_throws()
    {
        Skip.IfNot(_fixture.IsAvailable, $"k3s unavailable: {_fixture.UnavailableReason}");

        const string ns = "coding-agents-it";
        await EnsureNamespaceAsync(ns);
        await EnsureServiceAccountAsync(ns, "coding-agent-sandbox");

        var opts = new KubernetesSandboxOptions
        {
            Image = "busybox:1.36",
            Namespace = ns,
            ReadOnlyRootFilesystem = false,
            PodReadyTimeout = TimeSpan.FromSeconds(120),
        };

        string? podName = null;
        try
        {
            await using var sandbox = BuildSandbox(opts);
            (await sandbox.StartAsync(new SandboxOptions { Kind = SandboxKind.KubernetesPod })).IsSuccess.Should().BeTrue();
            podName = sandbox.PodName;
            throw new InvalidOperationException("simulate caller failure");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        podName.Should().NotBeNull();
        // Give kube a moment to process the delete propagation.
        await Task.Delay(2_000);
        await FluentActions.Awaiting(async () =>
            await _fixture.Client.CoreV1.ReadNamespacedPodAsync(podName!, ns))
            .Should().ThrowAsync<k8s.Autorest.HttpOperationException>();
    }

    [SkippableFact]
    public async Task ExecBashAsync_returns_nonzero_exit_for_failing_command()
    {
        Skip.IfNot(_fixture.IsAvailable, $"k3s unavailable: {_fixture.UnavailableReason}");

        const string ns = "coding-agents-it";
        await EnsureNamespaceAsync(ns);
        await EnsureServiceAccountAsync(ns, "coding-agent-sandbox");

        var opts = new KubernetesSandboxOptions
        {
            Image = "busybox:1.36",
            Namespace = ns,
            ReadOnlyRootFilesystem = false,
            PodReadyTimeout = TimeSpan.FromSeconds(120),
        };

        await using var sandbox = BuildSandbox(opts);
        (await sandbox.StartAsync(new SandboxOptions { Kind = SandboxKind.KubernetesPod })).IsSuccess.Should().BeTrue();

        var result = await sandbox.ExecBashAsync("exit 42");
        result.IsSuccess.Should().BeTrue();
        result.Value.ExitCode.Should().Be(42);
        result.Value.IsSuccess.Should().BeFalse();
    }
}
