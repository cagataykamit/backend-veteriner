using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.ExampleClinic;
using Backend.Veteriner.Application.Tenants.Queries.GetExampleClinicStatus;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>Örnek klinik durumu (§10.2): temizlikle aynı yetki ve kiracı eşleşmesi; sayılar temizleyiciden aynen döner.</summary>
public sealed class GetExampleClinicStatusQueryHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IExampleClinicCleaner> _cleaner = new();

    private GetExampleClinicStatusQueryHandler CreateHandler()
        => new(_tenantContext.Object, _permissions.Object, _cleaner.Object);

    [Fact]
    public async Task Handle_Should_ReturnPermissionDenied_When_SubscriptionsManageMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(false);
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);

        var result = await CreateHandler().Handle(new GetExampleClinicStatusQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        VerifyCleanerNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnContextMissing_When_JwtTenantAbsent()
    {
        SetupAuthorized(jwtTenantId: null);

        var result = await CreateHandler().Handle(new GetExampleClinicStatusQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        VerifyCleanerNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnAccessDenied_When_RouteTenantDiffersFromJwt()
    {
        SetupAuthorized(jwtTenantId: Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetExampleClinicStatusQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
        VerifyCleanerNeverCalled();
    }

    [Fact]
    public async Task Handle_Should_ReturnCleanerStatus_When_Authorized()
    {
        SetupAuthorized(_tenantId);
        var status = new ExampleClinicStatusDto(true, new ExampleClinicDataCountsDto(12, 20, 38, 26, 14, 9, 5, 48, 19, 10, 0, 29, 2));
        _cleaner.Setup(x => x.GetStatusAsync(_tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(status);

        var result = await CreateHandler().Handle(new GetExampleClinicStatusQuery(_tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsSuccess ? null : result.Error.Message);
        result.Value.Should().Be(status);
        _cleaner.Verify(x => x.ClearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupAuthorized(Guid? jwtTenantId)
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Subscriptions.Manage)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(jwtTenantId);
    }

    private void VerifyCleanerNeverCalled()
        => _cleaner.Verify(x => x.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
}
