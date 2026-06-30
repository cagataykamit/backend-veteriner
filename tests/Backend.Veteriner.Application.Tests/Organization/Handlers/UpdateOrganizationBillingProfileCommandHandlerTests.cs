using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Organization.Commands.UpdateBillingProfile;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Organization.Handlers;

public sealed class UpdateOrganizationBillingProfileCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<TenantBillingProfile>> _profilesRead = new();
    private readonly Mock<IRepository<TenantBillingProfile>> _profilesWrite = new();

    private UpdateOrganizationBillingProfileCommandHandler CreateHandler()
        => new(_tenant.Object, _permissions.Object, _profilesRead.Object, _profilesWrite.Object);

    private static UpdateOrganizationBillingProfileCommand ValidCommand(string? taxNumber = "1234567890")
        => new(
            "YağmurVet",
            "YağmurVet Veteriner Hizmetleri",
            "Kadıköy",
            taxNumber,
            "+905551234567",
            "İstanbul",
            "Kadıköy",
            "Caferağa",
            "Moda Cd.",
            "Vet Plaza",
            "12",
            "4");

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _profilesWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Create_When_RecordMissing()
    {
        var tenantId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _profilesRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantBillingProfile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantBillingProfile?)null);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().Be("YağmurVet");
        result.Value.TaxNumber.Should().Be("1234567890");
        _profilesWrite.Verify(x => x.AddAsync(It.IsAny<TenantBillingProfile>(), It.IsAny<CancellationToken>()), Times.Once);
        _profilesWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Update_When_RecordExists()
    {
        var tenantId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);

        var existing = TenantBillingProfile.CreateEmpty(tenantId);
        _profilesRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantBillingProfile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var cmd = ValidCommand("9876543210");
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TaxNumber.Should().Be("9876543210");
        existing.TaxNumber.Should().Be("9876543210");
        _profilesWrite.Verify(x => x.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Accept_EmptyOptionalFields()
    {
        var tenantId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        _profilesRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantBillingProfile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantBillingProfile?)null);

        var cmd = new UpdateOrganizationBillingProfileCommand(
            null, null, null, null, null, null, null, null, null, null, null, null);
        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CompanyName.Should().BeNull();
        result.Value.TaxNumber.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_UseTenantContext_NotForeignTenant()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenant.SetupGet(x => x.TenantId).Returns(tenantId);

        var foreignProfile = TenantBillingProfile.CreateEmpty(foreignTenantId);
        foreignProfile.Update("Foreign", null, null, "1111111111", null, null, null, null, null, null, null, null);

        _profilesRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantBillingProfile>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantBillingProfile?)null);

        var result = await CreateHandler().Handle(ValidCommand("2222222222"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _profilesWrite.Verify(
            x => x.AddAsync(It.Is<TenantBillingProfile>(p => p.TenantId == tenantId), It.IsAny<CancellationToken>()),
            Times.Once);
        result.Value!.TaxNumber.Should().Be("2222222222");
    }
}
