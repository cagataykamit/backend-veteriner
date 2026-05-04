using Backend.Veteriner.Application.Clinics.AppointmentSettings;
using Backend.Veteriner.Application.Clinics.Queries.AppointmentSettings.GetClinicAppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.AppointmentSettings;

public sealed class GetClinicAppointmentSettingsQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _settingsRead = new();

    public GetClinicAppointmentSettingsQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private GetClinicAppointmentSettingsQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _assignmentGuard.Object,
            _userClinics.Object,
            _clinicsRead.Object,
            _settingsRead.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId)
    {
        var c = new Clinic(tenantId, "K", "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(c, id);
        return c;
    }

    [Fact]
    public async Task Get_Should_ReturnDefaults_When_NoRow()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));
        _settingsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAppointmentSettings?)null);

        var result = await CreateHandler().Handle(new GetClinicAppointmentSettingsQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ClinicAppointmentSettingsDefaults.Build());
    }

    [Fact]
    public async Task Get_Should_ReturnAccessDenied_When_ClinicAdminUnassigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(uid);
        _assignmentGuard.Setup(x => x.MustApplyAssignedClinicScopeAsync(uid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(uid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));

        var result = await CreateHandler().Handle(new GetClinicAppointmentSettingsQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }
}
