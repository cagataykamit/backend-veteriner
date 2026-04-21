using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Queries.GetMemberById;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Faz 3A: tenant-scoped tek üye detayı.
/// Doğrulanan davranışlar:
/// <list type="bullet">
///   <item>Yetki yok → Auth.PermissionDenied (repository çağrılmaz).</item>
///   <item>TenantContext yok → Tenants.ContextMissing.</item>
///   <item>JWT tenant ≠ route tenant → Tenants.AccessDenied.</item>
///   <item>Üye bu kiracıda değilse (farklı kiracıda olsa bile) → 404 Members.NotFound (maskeleme).</item>
///   <item>Happy path: email/emailConfirmed ve <c>UserTenant.CreatedAtUtc</c> DTO'ya map edilir.</item>
///   <item>Roles alanı yalnız <c>InviteAssignableOperationClaimsCatalog</c> whitelist'ini içerir.</item>
///   <item>Clinics alanı <c>IUserClinicRepository.ListAccessibleClinicsAsync</c> sonucundan map edilir.</item>
/// </list>
/// </summary>
public sealed class GetTenantMemberByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<UserTenant>> _userTenants = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();

    private GetTenantMemberByIdQueryHandler CreateHandler()
        => new(_tenantContext.Object, _permissions.Object, _userTenants.Object,
               _userOperationClaims.Object, _userClinics.Object);

    private static UserTenant BuildMembership(Guid tenantId, Guid userId, string email, bool confirmed)
    {
        var user = new User(email, "hash");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, userId);
        if (confirmed) user.ConfirmEmail();

        var ut = new UserTenant(userId, tenantId);
        typeof(UserTenant).GetProperty(nameof(UserTenant.User))!.SetValue(ut, user);
        return ut;
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _userTenants.Verify(x => x.FirstOrDefaultAsync(It.IsAny<UserTenantByMemberSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_When_Member_Not_In_Tenant()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.FirstOrDefaultAsync(
                It.Is<UserTenantByMemberSpec>(s => s.TenantIdFilter == tid && s.UserIdFilter == mid),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserTenant?)null);

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(tid, mid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Members.NotFound");

        _userOperationClaims.Verify(x => x.GetDetailsByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userClinics.Verify(x => x.ListAccessibleClinicsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_MapCoreFields_On_HappyPath()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var membership = BuildMembership(tid, mid, "ali@klinik.com", confirmed: true);

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<UserTenantByMemberSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);
        _userOperationClaims.Setup(x => x.GetDetailsByUserIdAsync(mid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaimDetailDto>());
        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(mid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(tid, mid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(mid);
        result.Value.Email.Should().Be("ali@klinik.com");
        result.Value.Name.Should().Be("ali");
        result.Value.EmailConfirmed.Should().BeTrue();
        result.Value.CreatedAtUtc.Should().Be(membership.CreatedAtUtc);
        result.Value.Roles.Should().BeEmpty();
        result.Value.Clinics.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Map_Name_From_Email_LocalPart_With_Dotted_Prefix()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var membership = BuildMembership(tid, mid, "ahmet.yilmaz@acme.com", confirmed: true);

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<UserTenantByMemberSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);
        _userOperationClaims.Setup(x => x.GetDetailsByUserIdAsync(mid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaimDetailDto>());
        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(mid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(tid, mid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("ahmet.yilmaz");
    }

    [Fact]
    public async Task Handle_Should_Filter_Roles_By_Whitelist()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var membership = BuildMembership(tid, mid, "vet@klinik.com", confirmed: true);

        var claimInternalId = Guid.NewGuid();
        var claimVetId = Guid.NewGuid();
        var claimAdminId = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<UserTenantByMemberSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);
        _userOperationClaims.Setup(x => x.GetDetailsByUserIdAsync(mid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaimDetailDto>
            {
                new(Guid.NewGuid(), mid, "vet@klinik.com", claimInternalId, "Admin.Diagnostics"),
                new(Guid.NewGuid(), mid, "vet@klinik.com", claimVetId, "Veteriner"),
                new(Guid.NewGuid(), mid, "vet@klinik.com", claimAdminId, "Admin"),
            });
        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(mid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(tid, mid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Roles.Select(r => r.OperationClaimName).Should().BeEquivalentTo(new[] { "Veteriner", "Admin" });
        result.Value.Roles.Should().NotContain(r => r.OperationClaimName == "Admin.Diagnostics");
    }

    [Fact]
    public async Task Handle_Should_Map_Clinic_List()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var membership = BuildMembership(tid, mid, "sek@klinik.com", confirmed: true);

        var merkez = new Clinic(tid, "Merkez", "Istanbul");
        var sube = new Clinic(tid, "Şube", "Ankara");
        sube.Deactivate();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenants.Setup(x => x.FirstOrDefaultAsync(It.IsAny<UserTenantByMemberSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);
        _userOperationClaims.Setup(x => x.GetDetailsByUserIdAsync(mid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaimDetailDto>());
        _userClinics.Setup(x => x.ListAccessibleClinicsAsync(mid, tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { merkez, sube });

        var result = await CreateHandler().Handle(
            new GetTenantMemberByIdQuery(tid, mid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Clinics.Should().HaveCount(2);
        result.Value.Clinics.Should().ContainSingle(c => c.ClinicId == merkez.Id && c.Name == "Merkez" && c.IsActive);
        result.Value.Clinics.Should().ContainSingle(c => c.ClinicId == sube.Id && c.Name == "Şube" && !c.IsActive);
    }
}
