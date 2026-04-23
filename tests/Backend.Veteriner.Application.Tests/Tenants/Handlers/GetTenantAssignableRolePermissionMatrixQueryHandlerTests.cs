using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Queries.GetAssignableRolePermissionMatrix;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Authorization;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantAssignableRolePermissionMatrixQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<OperationClaim>> _claims = new();
    private readonly Mock<IReadRepository<OperationClaimPermission>> _claimPermissions = new();

    private GetTenantAssignableRolePermissionMatrixQueryHandler CreateHandler()
        => new(_tenantContext.Object, _permissions.Object, _claims.Object, _claimPermissions.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var r = await CreateHandler().Handle(
            new GetTenantAssignableRolePermissionMatrixQuery(Guid.NewGuid()), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Auth.PermissionDenied");
        _claims.Verify(x => x.ListAsync(It.IsAny<AssignableInviteOperationClaimsByNameSetSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var r = await CreateHandler().Handle(
            new GetTenantAssignableRolePermissionMatrixQuery(Guid.NewGuid()), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var r = await CreateHandler().Handle(
            new GetTenantAssignableRolePermissionMatrixQuery(Guid.NewGuid()), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnOrderedRows_WithPermissionsFromDb()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var vetId = Guid.NewGuid();
        var vetClaim = new OperationClaim("Veteriner");
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(vetClaim, vetId);

        _claims.Setup(x => x.ListAsync(It.IsAny<AssignableInviteOperationClaimsByNameSetSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OperationClaim> { vetClaim });

        var perm = new Permission("Pets.Read", "Hayvan oku", "Pets");
        var link = new OperationClaimPermission(vetId, perm.Id);
        typeof(OperationClaimPermission).GetProperty(nameof(OperationClaimPermission.Permission))!.SetValue(link, perm);

        _claimPermissions.Setup(x => x.ListAsync(
                It.IsAny<OperationClaimPermissionsWithPermissionsByClaimIdsSpec>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OperationClaimPermission> { link });

        var r = await CreateHandler().Handle(new GetTenantAssignableRolePermissionMatrixQuery(tid), CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Should().ContainSingle();
        var row = r.Value[0];
        row.OperationClaimId.Should().Be(vetId);
        row.OperationClaimName.Should().Be("Veteriner");
        row.Permissions.Should().ContainSingle(p => p.Code == "Pets.Read" && p.Description == "Hayvan oku" && p.Group == "Pets");
    }
}
