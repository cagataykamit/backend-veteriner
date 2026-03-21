using System.Security.Claims;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Tenancy;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Tenancy;

public sealed class TenantRequestResolverTests
{
    [Fact]
    public void Resolve_Should_UseClaim_When_QueryMissing()
    {
        var tid = Guid.NewGuid();
        var claims = new[] { new Claim(VeterinerClaims.TenantId, tid.ToString("D")) };

        var r = TenantRequestResolver.Resolve(claims, null);

        r.TenantConflict.Should().BeFalse();
        r.TenantId.Should().Be(tid);
    }

    [Fact]
    public void Resolve_Should_UseQuery_When_ClaimMissing()
    {
        var tid = Guid.NewGuid();

        var r = TenantRequestResolver.Resolve(Array.Empty<Claim>(), tid.ToString("D"));

        r.TenantConflict.Should().BeFalse();
        r.TenantId.Should().Be(tid);
    }

    [Fact]
    public void Resolve_Should_PreferClaim_When_BothMatch()
    {
        var tid = Guid.NewGuid();
        var claims = new[] { new Claim(VeterinerClaims.TenantId, tid.ToString("D")) };

        var r = TenantRequestResolver.Resolve(claims, tid.ToString("D"));

        r.TenantConflict.Should().BeFalse();
        r.TenantId.Should().Be(tid);
    }

    [Fact]
    public void Resolve_Should_Conflict_When_ClaimAndQueryDiffer()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var claims = new[] { new Claim(VeterinerClaims.TenantId, a.ToString("D")) };

        var r = TenantRequestResolver.Resolve(claims, b.ToString("D"));

        r.TenantConflict.Should().BeTrue();
        r.TenantId.Should().BeNull();
    }

    [Fact]
    public void Resolve_Should_ReturnNull_When_NeitherProvided()
    {
        var r = TenantRequestResolver.Resolve(Array.Empty<Claim>(), null);

        r.TenantConflict.Should().BeFalse();
        r.TenantId.Should().BeNull();
    }

    [Fact]
    public void Resolve_Should_IgnoreInvalidClaimString()
    {
        var claims = new[] { new Claim(VeterinerClaims.TenantId, "not-a-guid") };
        var tid = Guid.NewGuid();

        var r = TenantRequestResolver.Resolve(claims, tid.ToString("D"));

        r.TenantConflict.Should().BeFalse();
        r.TenantId.Should().Be(tid);
    }
}
