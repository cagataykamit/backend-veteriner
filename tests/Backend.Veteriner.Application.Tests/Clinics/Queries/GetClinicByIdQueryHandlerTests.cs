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
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();

    public GetClinicByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private GetClinicByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _assignmentGuard.Object,
            _userClinics.Object,
            _clinicsRead.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId)
    {
        var clinic = new Clinic(tenantId, "Merkez", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        return clinic;
    }

    [Fact]
    public async Task Should_ReturnDetail_When_ScopeOff()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(clinicId, tenantId));

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
    }

    [Fact]
    public async Task Should_ReturnAccessDenied_When_ClinicAdminScope_NotAssigned()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(userId);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(clinicId, tenantId));

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Should_Succeed_When_ClinicAdminScope_Assigned()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(userId);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(clinicId, tenantId));

        var result = await CreateHandler().Handle(new GetClinicByIdQuery(clinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
    }
}
