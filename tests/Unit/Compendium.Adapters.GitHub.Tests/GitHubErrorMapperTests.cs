// -----------------------------------------------------------------------
// <copyright file="GitHubErrorMapperTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Compendium.Adapters.GitHub.Http;
using NSubstitute;
using Octokit;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubErrorMapperTests
{
    [Fact]
    public void FromStatus_409_MapsToConflict()
    {
        GitHubErrorMapper.FromStatus(409, new GitRestErrorContext { ConflictResource = "acme/x" }, "exists")
            .Code.Should().Be("Git.Conflict");
    }

    [Fact]
    public void FromStatus_422WithoutConflictWording_MapsToProviderRejected()
    {
        GitHubErrorMapper.FromStatus(422, GitRestErrorContext.None, "some other validation error")
            .Code.Should().Be("Git.ProviderRejected");
    }

    [Fact]
    public void FromStatus_403WithoutRateLimit_MapsToProviderRejected()
    {
        GitHubErrorMapper.FromStatus(403, GitRestErrorContext.None, "forbidden")
            .Code.Should().Be("Git.ProviderRejected");
    }

    [Fact]
    public void FromException_NotFound_UsesRepositoryContext()
    {
        var ex = new NotFoundException(FakeResponse(HttpStatusCode.NotFound));

        GitHubErrorMapper.FromException(ex, GitRestErrorContext.ForRepository(new GitRepositoryRef("a", "b")))
            .Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public void FromException_NotFound_UsesNamespaceContext()
    {
        var ex = new NotFoundException(FakeResponse(HttpStatusCode.NotFound));

        GitHubErrorMapper.FromException(ex, GitRestErrorContext.ForNamespace("acme"))
            .Code.Should().Be("Git.NamespaceNotFound");
    }

    [Fact]
    public void FromException_NotFound_WithoutContext_MapsToProviderRejected()
    {
        var ex = new NotFoundException(FakeResponse(HttpStatusCode.NotFound));

        GitHubErrorMapper.FromException(ex, GitRestErrorContext.None).Code.Should().Be("Git.ProviderRejected");
    }

    [Fact]
    public void FromException_Authorization_MapsToAuthenticationFailed()
    {
        var ex = new AuthorizationException(FakeResponse(HttpStatusCode.Unauthorized));

        GitHubErrorMapper.FromException(ex, GitRestErrorContext.None).Code.Should().Be("Git.AuthenticationFailed");
    }

    [Fact]
    public void FromException_ValidationAlreadyExists_MapsToConflict()
    {
        var ex = new ApiValidationException(
            FakeResponse(HttpStatusCode.UnprocessableEntity, "{\"message\":\"Reference already exists\"}"));

        GitHubErrorMapper.FromException(ex, new GitRestErrorContext { ConflictResource = "acme/x" })
            .Code.Should().Be("Git.Conflict");
    }

    [Fact]
    public void FromException_GenericApiException_MapsToProviderRejected()
    {
        var ex = new ApiException(FakeResponse(HttpStatusCode.InternalServerError));

        var error = GitHubErrorMapper.FromException(ex, GitRestErrorContext.None);
        error.Code.Should().Be("Git.ProviderRejected");
        error.Metadata["statusCode"].Should().Be(500);
    }

    [Fact]
    public void FromException_UnknownException_MapsToProviderRejected()
    {
        GitHubErrorMapper.FromException(new InvalidOperationException("boom"), GitRestErrorContext.None)
            .Code.Should().Be("Git.ProviderRejected");
    }

    [Fact]
    public void FromException_RateLimit_MapsToThrottledWithReset()
    {
        var reset = DateTimeOffset.UtcNow.AddSeconds(90).ToUnixTimeSeconds();
        var apiInfo = new ApiInfo(
            new Dictionary<string, Uri>(), [], [], "etag", new RateLimit(5000, 0, reset), TimeSpan.Zero);
        var response = FakeResponse(HttpStatusCode.Forbidden);
        response.ApiInfo.Returns(apiInfo);

        var error = GitHubErrorMapper.FromException(new RateLimitExceededException(response), GitRestErrorContext.None);

        error.Code.Should().Be("Git.Throttled");
        error.Metadata.Should().ContainKey("retryAfterSeconds");
    }

    [Fact]
    public void FromException_Abuse_MapsToThrottled()
    {
        var response = FakeResponse(
            HttpStatusCode.Forbidden, headers: new Dictionary<string, string> { ["Retry-After"] = "30" });

        GitHubErrorMapper.FromException(new AbuseException(response), GitRestErrorContext.None)
            .Code.Should().Be("Git.Throttled");
    }

    [Fact]
    public void FromException_SecondaryRateLimit_MapsToThrottled()
    {
        GitHubErrorMapper.FromException(
            new SecondaryRateLimitExceededException(FakeResponse(HttpStatusCode.Forbidden)), GitRestErrorContext.None)
            .Code.Should().Be("Git.Throttled");
    }

    [Fact]
    public void FromException_ValidationWithErrorDetails_MapsToConflict()
    {
        const string body = """
        {"message":"Validation Failed","errors":[{"resource":"Repository","field":"name","code":"custom","message":"name already exists"}]}
        """;

        var error = GitHubErrorMapper.FromException(
            new ApiValidationException(FakeResponse(HttpStatusCode.UnprocessableEntity, body)),
            new GitRestErrorContext { ConflictResource = "acme/x" });

        error.Code.Should().Be("Git.Conflict");
    }

    private static IResponse FakeResponse(
        HttpStatusCode statusCode, string body = "", IDictionary<string, string>? headers = null)
    {
        var response = Substitute.For<IResponse>();
        response.StatusCode.Returns(statusCode);
        response.Body.Returns(body);
        response.ContentType.Returns("application/json");
        response.Headers.Returns((IReadOnlyDictionary<string, string>)(headers ?? new Dictionary<string, string>()));
        return response;
    }
}
