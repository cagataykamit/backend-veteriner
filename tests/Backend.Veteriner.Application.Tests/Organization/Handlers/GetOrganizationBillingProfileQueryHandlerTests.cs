using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;
using Backend.Veteriner.Application.Organization.Queries.GetBillingProfile;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Organization.Handlers;

public sealed class GetOrganizationBillingProfileQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<TenantBillingProfile>> _profilesRead = new();

    private GetOrganizationBillingProfileQueryHandler CreateHandler()
        => new(_tenant.Object, _permissions.Object, _profilesRead.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetOrganizationBillingProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionDenied()
    {
        _tenant.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(false);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateHandler().Handle(new GetOrganizationBillingProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyDto_When_RecordMissing()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.Read)).Returns(true);
        _profilesRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantBillingProfile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantBillingProfile?)null);

        var result = await CreateHandler().Handle(new GetOrganizationBillingProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().BeNull();
        result.Value.TaxNumber.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_ReturnStoredProfile_When_RecordExists()
    {
        var tenantId = Guid.NewGuid();
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);

        var profile = TenantBillingProfile.CreateEmpty(tenantId);
        profile.Update(
            "YağmurVet",
            "YağmurVet Veteriner Hizmetleri",
            "Kadıköy",
            "1234567890",
            "+905551234567",
            "İstanbul",
            "Kadıköy",
            "Caferağa",
            "Moda Cd.",
            "Vet Plaza",
            "12",
            "4");

        _profilesRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantBillingProfile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var result = await CreateHandler().Handle(new GetOrganizationBillingProfileQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("YağmurVet");
        result.Value.LegalCompanyName.Should().Be("YağmurVet Veteriner Hizmetleri");
        result.Value.TaxNumber.Should().Be("1234567890");
        result.Value.InvoiceProvince.Should().Be("İstanbul");
    }
}
