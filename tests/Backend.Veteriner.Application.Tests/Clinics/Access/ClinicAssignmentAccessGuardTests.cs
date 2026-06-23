using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Access;

public sealed class ClinicAssignmentAccessGuardTests
{
    private readonly Mock<IUserOperationClaimRepository> _claims = new();

    private ClinicAssignmentAccessGuard CreateSut() => new(_claims.Object);

    private void SetupClaims(Guid userId, params string[] names)
    {
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("Owner")]
    [InlineData("PlatformAdmin")]
    public async Task MustApply_Should_BeFalse_When_UserHasTenantWideClaim(string claimName)
    {
        var userId = Guid.NewGuid();
        SetupClaims(userId, claimName);

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MustApply_Should_BeFalse_When_UserHasAdminAndClinicAdmin_Claim()
    {
        var userId = Guid.NewGuid();
        SetupClaims(userId, "ClinicAdmin", "Admin");

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public async Task MustApply_Should_BeTrue_When_NonTenantWideRole(string claimName)
    {
        var userId = Guid.NewGuid();
        SetupClaims(userId, claimName);

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MustApply_Should_BeTrue_When_NoClaims()
    {
        var userId = Guid.NewGuid();
        SetupClaims(userId);

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MustApply_Should_PropagateCancellationToken_ToClaimRepository()
    {
        var userId = Guid.NewGuid();
        var ct = new CancellationTokenSource().Token;
        SetupClaims(userId, "Veteriner");

        await CreateSut().MustApplyAssignedClinicScopeAsync(userId, ct);

        _claims.Verify(
            x => x.GetOperationClaimNamesByUserIdAsync(userId, ct),
            Times.Once);
    }
}
