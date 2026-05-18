using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Access;

public sealed class ClinicAssignmentAccessGuardTests
{
    private readonly Mock<IUserOperationClaimRepository> _claims = new();

    private ClinicAssignmentAccessGuard CreateSut() => new(_claims.Object);

    [Fact]
    public async Task MustApply_Should_BeFalse_When_UserHasTenantAdminClaim()
    {
        var userId = Guid.NewGuid();
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Admin" });

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MustApply_Should_BeFalse_When_UserHasTenantAdminCaseInsensitive()
    {
        var userId = Guid.NewGuid();
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "ADMIN" });

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MustApply_Should_BeFalse_When_UserHasAdminAndClinicAdmin_Claim()
    {
        var userId = Guid.NewGuid();
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "ClinicAdmin", "Admin" });

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MustApply_Should_BeTrue_When_OnlyClinicAdminClaim()
    {
        var userId = Guid.NewGuid();
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "ClinicAdmin" });

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MustApply_Should_BeFalse_When_NoClinicAdminClaim()
    {
        var userId = Guid.NewGuid();
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Veteriner" });

        var result = await CreateSut().MustApplyAssignedClinicScopeAsync(userId, CancellationToken.None);

        result.Should().BeFalse();
    }
}
