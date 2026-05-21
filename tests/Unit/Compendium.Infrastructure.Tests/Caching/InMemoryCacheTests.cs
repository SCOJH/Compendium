// -----------------------------------------------------------------------
// <copyright file="InMemoryCacheTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Caching;
using Compendium.Infrastructure.Caching;
using Compendium.Multitenancy;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Compendium.Infrastructure.Tests.Caching;

public sealed class InMemoryCacheTests
{
    private static InMemoryCache CreateSut(out IMemoryCache memoryCache, ITenantContext? tenant = null)
    {
        memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new InMemoryCache(memoryCache, tenant);
    }

    [Fact]
    public void Ctor_NullMemoryCache_Throws()
    {
        Action act = () => _ = new InMemoryCache(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsSuccessWithNull()
    {
        var sut = CreateSut(out _);

        var result = await sut.GetAsync<string>("missing");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task SetAndGet_String_RoundTrip()
    {
        var sut = CreateSut(out _);

        await sut.SetAsync("k", "hello");
        var result = await sut.GetAsync<string>("k");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public async Task SetAndGet_Int_RoundTrip()
    {
        var sut = CreateSut(out _);

        await sut.SetAsync("n", 42);
        var result = await sut.GetAsync<int>("n");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    public sealed record Person(string Name, int Age);

    [Fact]
    public async Task SetAndGet_ComplexRecord_RoundTrip()
    {
        var sut = CreateSut(out _);
        var person = new Person("Ada", 36);

        await sut.SetAsync("person", person);
        var result = await sut.GetAsync<Person>("person");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(person);
    }

    [Fact]
    public async Task GetAsync_WhenStoredTypeDiffers_ReturnsNull()
    {
        var sut = CreateSut(out _);

        await sut.SetAsync("k", 42);

        // Stored an int; requesting a string → contract returns null (not a failure).
        var result = await sut.GetAsync<string>("k");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ConfiguresAbsoluteExpirationRelativeToNow_OnEntryOptions()
    {
        // Arrange — substitute IMemoryCache so we can inspect the configured options
        // without relying on wall-clock waits.
        var memoryCache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        entry.AbsoluteExpirationRelativeToNow.Returns((TimeSpan?)null);
        memoryCache.CreateEntry(Arg.Any<object>()).Returns(entry);

        var sut = new InMemoryCache(memoryCache);

        // Act
        var ttl = TimeSpan.FromMinutes(5);
        var result = await sut.SetAsync("k", "v", ttl);

        // Assert
        result.IsSuccess.Should().BeTrue();
        entry.Received().AbsoluteExpirationRelativeToNow = ttl;
    }

    [Fact]
    public async Task SetAsync_NoTtl_LeavesAbsoluteExpirationNull()
    {
        // The Set<T> overload always copies options onto the ICacheEntry — what we care
        // about is that AbsoluteExpirationRelativeToNow stays null (no expiration).
        TimeSpan? captured = TimeSpan.FromDays(999); // sentinel
        var memoryCache = Substitute.For<IMemoryCache>();
        var entry = Substitute.For<ICacheEntry>();
        entry.When(e => e.AbsoluteExpirationRelativeToNow = Arg.Any<TimeSpan?>())
             .Do(call => captured = call.Arg<TimeSpan?>());
        memoryCache.CreateEntry(Arg.Any<object>()).Returns(entry);

        var sut = new InMemoryCache(memoryCache);

        var result = await sut.SetAsync("k", "v");

        result.IsSuccess.Should().BeTrue();
        captured.Should().BeNull("entries with no TTL must not have an absolute expiration");
    }

    [Fact]
    public async Task SetAsync_LiveEntry_PersistsValueWithoutTtl()
    {
        // End-to-end check using a real MemoryCache: a value stored without TTL
        // can be retrieved immediately and on subsequent reads.
        var sut = CreateSut(out _);

        await sut.SetAsync("k", "v");

        (await sut.GetAsync<string>("k")).Value.Should().Be("v");
        (await sut.ExistsAsync("k")).Value.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SetAsync_NonPositiveTtl_ReturnsInvalidTtlFailure(int seconds)
    {
        var sut = CreateSut(out _);

        var result = await sut.SetAsync("k", "v", TimeSpan.FromSeconds(seconds));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Caching.InvalidTtl");
    }

    [Fact]
    public async Task SetAsync_ShortTtl_ExpiresAfterWait()
    {
        var sut = CreateSut(out _);

        await sut.SetAsync("k", "v", TimeSpan.FromMilliseconds(50));
        await Task.Delay(200);

        var result = await sut.GetAsync<string>("k");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_AfterSet_GetReturnsNull()
    {
        var sut = CreateSut(out _);

        await sut.SetAsync("k", "v");
        var remove = await sut.RemoveAsync("k");
        var get = await sut.GetAsync<string>("k");

        remove.IsSuccess.Should().BeTrue();
        get.Value.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_MissingKey_IsSuccess()
    {
        var sut = CreateSut(out _);

        var result = await sut.RemoveAsync("never-set");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_MissingKey_ReturnsFalse()
    {
        var sut = CreateSut(out _);

        var result = await sut.ExistsAsync("missing");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_AfterSet_ReturnsTrue()
    {
        var sut = CreateSut(out _);

        await sut.SetAsync("k", "v");
        var result = await sut.ExistsAsync("k");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NullOrWhitespaceKey_AcrossAllOps_Throws(string? key)
    {
        var sut = CreateSut(out _);

        var get = () => sut.GetAsync<string>(key!);
        var set = () => sut.SetAsync(key!, "v");
        var remove = () => sut.RemoveAsync(key!);
        var exists = () => sut.ExistsAsync(key!);

        await get.Should().ThrowAsync<ArgumentException>();
        await set.Should().ThrowAsync<ArgumentException>();
        await remove.Should().ThrowAsync<ArgumentException>();
        await exists.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TenantPrefix_KeysAreIsolatedBetweenTenants()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var tenantA = new TenantContext();
        tenantA.SetTenant(new TenantInfo { Id = "tenant-a" });
        var tenantB = new TenantContext();
        tenantB.SetTenant(new TenantInfo { Id = "tenant-b" });

        var cacheA = new InMemoryCache(memoryCache, tenantA);
        var cacheB = new InMemoryCache(memoryCache, tenantB);

        await cacheA.SetAsync("x", "from-A");

        (await cacheA.GetAsync<string>("x")).Value.Should().Be("from-A");
        (await cacheB.GetAsync<string>("x")).Value.Should().BeNull();
        (await cacheB.ExistsAsync("x")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task NoTenantContext_WritesAndReadsWithoutPrefix()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var sut = new InMemoryCache(memoryCache);

        await sut.SetAsync("x", "raw");

        // Sanity check: the underlying entry is stored at the unprefixed key.
        memoryCache.TryGetValue("x", out var raw).Should().BeTrue();
        raw.Should().Be("raw");

        (await sut.GetAsync<string>("x")).Value.Should().Be("raw");
    }

    [Fact]
    public async Task EmptyTenantId_IsTreatedAsNoTenant()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var emptyTenant = new TenantContext();
        emptyTenant.SetTenant(new TenantInfo { Id = string.Empty });

        var sut = new InMemoryCache(memoryCache, emptyTenant);

        await sut.SetAsync("x", "raw");

        memoryCache.TryGetValue("x", out var raw).Should().BeTrue();
        raw.Should().Be("raw");
    }

    [Fact]
    public async Task TenantPrefix_AppearsInUnderlyingKey()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var tenant = new TenantContext();
        tenant.SetTenant(new TenantInfo { Id = "acme" });

        var sut = new InMemoryCache(memoryCache, tenant);

        await sut.SetAsync("x", "v");

        memoryCache.TryGetValue("acme:x", out var raw).Should().BeTrue();
        raw.Should().Be("v");
        // The unprefixed key alone is not present.
        memoryCache.TryGetValue("x", out _).Should().BeFalse();
    }

    [Fact]
    public void AddInMemoryCache_RegistersICache_ResolvableFromServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryCache();

        using var sp = services.BuildServiceProvider();

        var cache = sp.GetService<ICache>();
        cache.Should().NotBeNull();
        cache.Should().BeOfType<InMemoryCache>();

        var memory = sp.GetService<IMemoryCache>();
        memory.Should().NotBeNull();
    }

    [Fact]
    public void AddInMemoryCache_WithConfigure_AppliesMemoryCacheOptions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryCache(opts => opts.SizeLimit = 123);

        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MemoryCacheOptions>>();
        options.Value.SizeLimit.Should().Be(123);

        sp.GetService<ICache>().Should().NotBeNull();
    }

    [Fact]
    public void AddInMemoryCache_NullServices_Throws()
    {
        IServiceCollection? services = null;
        Action act = () => services!.AddInMemoryCache();
        act.Should().Throw<ArgumentNullException>();
    }
}
