using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.ClearExampleClinic;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Örnek klinik temizleme kuralları (EXAMPLE_CLINIC_API_CONTRACT.md §10): yetki, kiracı eşleşmesi (IDOR), örnek veri yoksa
/// 409; başarıda temizleyici oturumdaki kiracıyla çağrılır ve sayıları döner.
/// </summary>
public sealed class ClearExampleClinicCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IExampleClinicCleaner> _cleaner = new();

    private ClearExampleClinicCommandHandler CreateHandler()
        => new(_tenantContext.Object, _permissions.Object, _cleaner.Object);

    private ClearExampleClinicCommand Command => new(_tenantId);

    [Fact]
    public async Task Handle_Should_ReturnPermissionDenied_When_SubscriptionsManageMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(false);
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        VerifyCleanerNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnContextMissing_When_JwtTenantAbsent()
    {
        SetupAuthorized(jwtTenantId: null);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        VerifyCleanerNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnAccessDenied_When_RouteTenantDiffersFromJwt()
    {
        SetupAuthorized(jwtTenantId: Guid.NewGuid());

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
        VerifyCleanerNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnNotLoaded_And_DeleteNothing_When_NoExampleData()
    {
        SetupAuthorized(_tenantId);
        _cleaner.Setup(x => x.GetStatusAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExampleClinicStatusDto(false, ExampleClinicDataCountsDto.Empty));

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ExampleClinicNotLoaded");
        _cleaner.Verify(x => x.ClearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ClearSessionTenant_And_ReturnCounts_When_ExampleDataExists()
    {
        SetupAuthorized(_tenantId);
        var preview = new ExampleClinicDataCountsDto(12, 20, 38, 26, 14, 9, 5, 48, 19, 10, 0, 29, 0);
        var cleared = preview with { Products = 9, ProductsDeactivated = 1 };
        _cleaner.Setup(x => x.GetStatusAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExampleClinicStatusDto(true, preview));
        _cleaner.Setup(x => x.ClearAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(cleared);

        var result = await CreateHandler().Handle(Command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? null : result.Error.Message);
        result.Value.Should().Be(cleared);
        _cleaner.Verify(x => x.ClearAsync(_tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Command_Should_BeAudited_As_ExampleClinicClear()
    {
        Command.AuditAction.Should().Be("Tenant.ExampleClinicClear");
        Command.AuditTarget.Should().Be($"TenantId={_tenantId}");
    }

    private void SetupAuthorized(Guid? jwtTenantId)
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(jwtTenantId);
    }

    private void VerifyCleanerNeverCalled()
    {
        _cleaner.Verify(x => x.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _cleaner.Verify(x => x.ClearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
