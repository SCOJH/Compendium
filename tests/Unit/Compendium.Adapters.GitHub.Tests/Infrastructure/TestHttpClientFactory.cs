// -----------------------------------------------------------------------
// <copyright file="TestHttpClientFactory.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> for tests. The adapter builds absolute
/// request URIs itself, so a plain client with no base address suffices.
/// </summary>
internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
