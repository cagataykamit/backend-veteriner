using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Moq;

namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary>İşletme yöneticisi (kiracı geneli) olarak <see cref="TenantMemberManagementAccess"/>: klinik, rol ve üye kısıtı uygulanmaz.</summary>
public static class TenantMemberManagementAccessStub
{
    public static TenantMemberManagementAccess TenantWide()
    {
        var client = new Mock<IClientContext>();
        client.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        var guard = new Mock<IClinicAssignmentAccessGuard>();
        guard.Setup(g => g.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return new TenantMemberManagementAccess(
            client.Object, guard.Object, Mock.Of<IUserClinicRepository>(), Mock.Of<IUserOperationClaimRepository>());
    }
}
