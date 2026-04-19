using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.UpdateSettings;
using Backend.Veteriner.Application.Tenants.Commands.UpdateSettings.Validators;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Faz 5B: tenant-scoped kurum ayarları — yalnızca <c>Name</c> güncellenebilir.
/// Ortak davranışlar: yetki / context / tenant match / tenant-not-found / duplicate-name.
/// Kendi id'si duplicate sayılmaz. Happy path success + güncel TenantDetailDto döner.
/// Read-only / cancelled tenant → merkezi <c>TenantSubscriptionWriteGuardBehavior</c> tarafından kesilir
/// (ayrı test burada yazılmaz; davranış "TenantSubscriptionWriteGuardBehaviorTests" kapsamında doğrulanmıştır).
/// </summary>
public sealed class UpdateTenantSettingsCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Tenant>> _read = new();
    private readonly Mock<IRepository<Tenant>> _write = new();

    private UpdateTenantSettingsCommandHandler CreateHandler()
        => new(_tenantContext.Object, _permissions.Object, _read.Object, _write.Object);

    private static Tenant BuildTenant(Guid id, string name = "Acme Vet")
    {
        var tenant = new Tenant(name);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, id);
        return tenant;
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(Guid.NewGuid(), "Yeni"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _read.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _write.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(Guid.NewGuid(), "Yeni"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _write.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(Guid.NewGuid(), "Yeni"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
        _read.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_Tenant_Missing()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(tid, "Yeni"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
        _write.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_CaseInsensitive_DifferentTenant()
    {
        var tid = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTenant(tid, "Acme Vet"));
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTenant(otherId, "Zeta Vet"));

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(tid, "zeta vet"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.DuplicateName");
        _write.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Succeed_HappyPath()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var existing = BuildTenant(tid, "Acme Vet");
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(tid, "Acme Veteriner A.Ş."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(tid);
        result.Value.Name.Should().Be("Acme Veteriner A.Ş.");
        existing.Name.Should().Be("Acme Veteriner A.Ş.");
        _write.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NotTreat_OwnId_AsDuplicate_When_Only_Casing_Changes()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var existing = BuildTenant(tid, "Acme Vet");
        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _read.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(
            new UpdateTenantSettingsCommand(tid, "ACME VET"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("ACME VET");
        existing.Name.Should().Be("ACME VET");
        _write.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Validator_Should_Reject_Empty_TenantId_And_Short_Name()
    {
        var validator = new UpdateTenantSettingsCommandValidator();

        var emptyId = validator.Validate(new UpdateTenantSettingsCommand(Guid.Empty, "Acme Vet"));
        emptyId.IsValid.Should().BeFalse();
        emptyId.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateTenantSettingsCommand.TenantId));

        var shortName = validator.Validate(new UpdateTenantSettingsCommand(Guid.NewGuid(), "A"));
        shortName.IsValid.Should().BeFalse();
        shortName.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateTenantSettingsCommand.Name));

        var empty = validator.Validate(new UpdateTenantSettingsCommand(Guid.NewGuid(), ""));
        empty.IsValid.Should().BeFalse();
    }
}
