using Backend.Veteriner.Application.Clinics.Queries.GetById;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Queries;

public sealed class GetClinicByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetClinicByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        // Varsayılan: kullanıcının operation claim'i yok (tenant-wide değil).
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetClinicByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _clinicsRead.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId)
    {
        var clinic = new Clinic(tenantId, "Merkez", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private void SetupClinicFound(Guid clinicId, Guid tenantId)
        => _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(clinicId, tenantId));

    private void SetupTenantWideRole(params string[] claimNames)
        => _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimNames);

    private void SetupAssignment(Guid clinicId, bool assigned)
        => _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

    [Fact]
    public async Task Admin_Should_Read_UnassignedClinic_InOwnTenant()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupTenantWideRole("Admin");
        SetupAssignment(clinicId, assigned: false);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide kullanıcı için UserClinic kontrolü çalıştırılmamalı");
    }

    [Fact]
    public async Task Owner_Should_Read_UnassignedClinic_InOwnTenant()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupTenantWideRole("Owner");
        SetupAssignment(clinicId, assigned: false);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
    }

    [Fact]
    public async Task PlatformAdmin_Should_Read_ClinicInActiveTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupTenantWideRole("PlatformAdmin");
        SetupAssignment(clinicId, assigned: false);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
    }

    [Fact]
    public async Task NonTenantWideUser_Should_Read_AssignedClinic()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupAssignment(clinicId, assigned: true);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
    }

    [Fact]
    public async Task NonTenantWideUser_Should_NotRead_UnassignedClinic()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupAssignment(clinicId, assigned: false);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Veteriner_WithClinicsRead_Should_NotRead_UnassignedClinic()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupTenantWideRole("Veteriner");
        SetupAssignment(clinicId, assigned: false);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Sekreter_WithClinicsRead_Should_NotRead_UnassignedClinic()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupTenantWideRole("Sekreter");
        SetupAssignment(clinicId, assigned: false);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task ClinicFromOtherTenant_Should_NotBeReturned_ToAnyUser()
    {
        // ClinicByIdSpec tenant filtresi nedeniyle başka tenant'ın kliniği hiç dönmez (null).
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);
        SetupTenantWideRole("Admin");

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task ClinicNotFound_Should_Return_ClinicsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupClinicFound(clinicId, tenantId);
        SetupAssignment(clinicId, assigned: false);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), token);

        _clinicsRead.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }

    [Fact]
    public async Task Should_MapProfileFields_OnDetail()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clinic = BuildClinic(clinicId, tenantId);
        clinic.UpdateDetails(
            "Merkez",
            "İstanbul",
            "+90 212",
            "klinik@test.com",
            "Adres",
            "Tanım");
        SetupTenant(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        SetupTenantWideRole("Admin");

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Phone.Should().Be("+90 212");
        result.Value.Email.Should().Be("klinik@test.com");
        result.Value.Address.Should().Be("Adres");
        result.Value.Description.Should().Be("Tanım");
    }
}
