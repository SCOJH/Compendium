// -----------------------------------------------------------------------
// <copyright file="StubClientProvider.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Http;
using Octokit;

namespace Compendium.Adapters.GitHub.Tests.Infrastructure;

/// <summary>
/// An <see cref="IGitHubClientProvider"/> that hands back a fixed Octokit client
/// (real, pointed at WireMock) or a preset failure — so the Octokit-backed
/// services can be exercised against recorded HTTP responses.
/// </summary>
internal sealed class StubClientProvider : IGitHubClientProvider
{
    private readonly Result<IGitHubClient> _result;

    private StubClientProvider(Result<IGitHubClient> result) => _result = result;

    public static StubClientProvider Returning(IGitHubClient client) =>
        new(Result.Success(client));

    public static StubClientProvider Failing(Error error) =>
        new(Result.Failure<IGitHubClient>(error));

    public Task<Result<IGitHubClient>> GetClientAsync(GitConnection connection, CancellationToken cancellationToken) =>
        Task.FromResult(_result);
}
