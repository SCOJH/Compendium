// -----------------------------------------------------------------------
// <copyright file="TenantPropagatingDelegatingHandlerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http;
using Compendium.Multitenancy.Http;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="TenantPropagatingDelegatingHandler"/> class.
/// </summary>
public class TenantPropagatingDelegatingHandlerTests
{
    private readonly ITenantContextAccessor _accessor = Substitute.For<ITenantContextAccessor>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ILogger<TenantPropagatingDelegatingHandler> _logger
        = Substitute.For<ILogger<TenantPropagatingDelegatingHandler>>();

    public TenantPropagatingDelegatingHandlerTests()
    {
        _accessor.TenantContext.Returns(_tenantContext);
    }

    [Fact]
    public void TenantPropagatingDelegatingHandler_Constructor_WithNullAccessor_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new TenantPropagatingDelegatingHandler(null!, new TenantPropagationOptions(), _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantContextAccessor");
    }

    [Fact]
    public void TenantPropagatingDelegatingHandler_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new TenantPropagatingDelegatingHandler(_accessor, null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void TenantPropagatingDelegatingHandler_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new TenantPropagatingDelegatingHandler(_accessor, new TenantPropagationOptions(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task SendAsync_WhenNoTenantContext_DoesNotAddHeaders()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(false);

        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, new TenantPropagationOptions(), _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Headers.Contains("X-Tenant-ID").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenTenantSet_AddsTenantIdHeader()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns((string?)null);
        _tenantContext.CurrentTenant.Returns(new TenantInfo { Id = "tenant-1", Name = "T1" });

        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, new TenantPropagationOptions(), _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Headers.GetValues("X-Tenant-ID").Should().ContainSingle().Which.Should().Be("tenant-1");
    }

    [Fact]
    public async Task SendAsync_WhenTenantIdHeaderAlreadyPresent_DoesNotOverride()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns((string?)null);
        _tenantContext.CurrentTenant.Returns(new TenantInfo { Id = "tenant-1" });

        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, new TenantPropagationOptions(), _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.Add("X-Tenant-ID", "preset");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest!.Headers.GetValues("X-Tenant-ID").Should().ContainSingle().Which.Should().Be("preset");
    }

    [Fact]
    public async Task SendAsync_WhenTenantNameEnabledAndPresent_AddsTenantNameHeader()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns("Acme");
        _tenantContext.CurrentTenant.Returns(new TenantInfo { Id = "tenant-1", Name = "Acme" });

        var options = new TenantPropagationOptions { IncludeTenantName = true };
        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, options, _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest!.Headers.GetValues("X-Tenant-Name").Should().ContainSingle().Which.Should().Be("Acme");
    }

    [Fact]
    public async Task SendAsync_WhenTenantNameEnabledButHeaderAlreadyPresent_DoesNotDuplicate()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns("Acme");
        _tenantContext.CurrentTenant.Returns(new TenantInfo { Id = "tenant-1", Name = "Acme" });

        var options = new TenantPropagationOptions { IncludeTenantName = true };
        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, options, _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.Add("X-Tenant-Name", "Preset");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest!.Headers.GetValues("X-Tenant-Name").Should().ContainSingle().Which.Should().Be("Preset");
    }

    [Fact]
    public async Task SendAsync_WhenTenantNameNullEvenIfIncludeRequested_DoesNotAddHeader()
    {
        // Arrange
        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns((string?)null);
        _tenantContext.CurrentTenant.Returns(new TenantInfo { Id = "tenant-1" });

        var options = new TenantPropagationOptions { IncludeTenantName = true };
        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, options, _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest!.Headers.Contains("X-Tenant-Name").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_WhenPropagateCustomPropertiesEnabled_AddsAllowedHeaders()
    {
        // Arrange
        var tenant = new TenantInfo
        {
            Id = "tenant-1",
            Name = "Acme",
            Properties = new Dictionary<string, object?>
            {
                ["Region"] = "eu-west-1",
                ["Tier"] = "gold",
                ["Internal"] = "secret",
                ["NumericValue"] = 42 // not a string -> should be skipped
            }
        };

        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns("Acme");
        _tenantContext.CurrentTenant.Returns(tenant);

        var options = new TenantPropagationOptions
        {
            PropagateCustomProperties = true,
            AllowedPropertyHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Region", "Tier", "NumericValue" }
        };

        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, options, _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest!.Headers.GetValues("X-Tenant-Region").Should().ContainSingle().Which.Should().Be("eu-west-1");
        inner.LastRequest.Headers.GetValues("X-Tenant-Tier").Should().ContainSingle().Which.Should().Be("gold");
        inner.LastRequest.Headers.Contains("X-Tenant-Internal").Should().BeFalse(); // not allowed
        inner.LastRequest.Headers.Contains("X-Tenant-NumericValue").Should().BeFalse(); // not a string
    }

    [Fact]
    public async Task SendAsync_WhenCustomPropertyHeaderAlreadyPresent_DoesNotOverride()
    {
        // Arrange
        var tenant = new TenantInfo
        {
            Id = "tenant-1",
            Properties = new Dictionary<string, object?> { ["Region"] = "eu" }
        };

        _tenantContext.HasTenant.Returns(true);
        _tenantContext.TenantId.Returns("tenant-1");
        _tenantContext.TenantName.Returns((string?)null);
        _tenantContext.CurrentTenant.Returns(tenant);

        var options = new TenantPropagationOptions
        {
            PropagateCustomProperties = true,
            AllowedPropertyHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Region" }
        };

        var inner = new RecordingHandler();
        var handler = new TenantPropagatingDelegatingHandler(_accessor, options, _logger)
        {
            InnerHandler = inner
        };
        var client = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.Add("X-Tenant-Region", "preset");

        // Act
        await client.SendAsync(request, CancellationToken.None);

        // Assert
        inner.LastRequest!.Headers.GetValues("X-Tenant-Region").Should().ContainSingle().Which.Should().Be("preset");
    }

    [Fact]
    public void TenantPropagationOptions_Defaults_AreSensible()
    {
        // Arrange & Act
        var options = new TenantPropagationOptions();

        // Assert
        options.TenantIdHeaderName.Should().Be("X-Tenant-ID");
        options.TenantNameHeaderName.Should().Be("X-Tenant-Name");
        options.IncludeTenantName.Should().BeFalse();
        options.PropagateCustomProperties.Should().BeFalse();
        options.AllowedPropertyHeaders.Should().BeEmpty();
    }

    [Fact]
    public void TenantPropagationOptions_AllowedPropertyHeaders_IsCaseInsensitive()
    {
        // Arrange
        var options = new TenantPropagationOptions();

        // Act
        options.AllowedPropertyHeaders.Add("Region");

        // Assert
        options.AllowedPropertyHeaders.Contains("region").Should().BeTrue();
        options.AllowedPropertyHeaders.Contains("REGION").Should().BeTrue();
    }

    /// <summary>
    /// Records the last request observed and returns 200 OK.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
