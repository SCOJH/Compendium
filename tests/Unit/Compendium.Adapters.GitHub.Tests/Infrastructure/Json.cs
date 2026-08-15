// -----------------------------------------------------------------------
// <copyright file="Json.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests.Infrastructure;

/// <summary>
/// WireMock JSON response builders that set <c>Content-Type: application/json</c>
/// — WireMock's <c>WithBodyAsJson</c> alone leaves it unset, which stops Octokit
/// from deserializing nested objects.
/// </summary>
internal static class Json
{
    public static IResponseBuilder Ok(object body) => Status(200, body);

    public static IResponseBuilder Created(object body) => Status(201, body);

    public static IResponseBuilder Status(int statusCode, object body) =>
        Response.Create().WithStatusCode(statusCode)
            .WithHeader("Content-Type", "application/json").WithBodyAsJson(body);
}
