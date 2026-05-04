using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.AppointmentSettings;

public sealed class UpdateClinicAppointmentSettingsCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IReadRepository<ClinicAppointmentSettings>> _settingsRead = new();
    private readonly Mock<IRepository<ClinicAppointmentSettings>> _settingsWrite = new();

    public UpdateClinicAppointmentSettingsCommandHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(true);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private UpdateClinicAppointmentSettingsCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _permissions.Object,
            _assignmentGuard.Object,
            _userClinics.Object,
            _clinicsRead.Object,
            _settingsRead.Object,
            _settingsWrite.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId)
    {
        var c = new Clinic(tenantId, "K", "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(c, id);
        return c;
    }

    [Fact]
    public async Task Update_Should_Create_When_NotExists()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));
        _settingsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAppointmentSettings?)null);

        var result = await CreateHandler().Handle(
            new UpdateClinicAppointmentSettingsCommand(cid, 45, 15, false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _settingsWrite.Verify(x => x.AddAsync(It.IsAny<ClinicAppointmentSettings>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Modify_When_Exists()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));

        var existing = ClinicAppointmentSettings.Create(tid, cid, 30, 15, false);
        _settingsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicAppointmentSettingsByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(
            new UpdateClinicAppointmentSettingsCommand(cid, 60, 30, true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DefaultAppointmentDurationMinutes.Should().Be(60);
        result.Value.SlotIntervalMinutes.Should().Be(30);
        result.Value.AllowOverlappingAppointments.Should().BeTrue();
        _settingsWrite.Verify(x => x.AddAsync(It.IsAny<ClinicAppointmentSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Deny_When_ClinicAdminUnassigned()
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

        var result = await CreateHandler().Handle(
            new UpdateClinicAppointmentSettingsCommand(cid, 30, 15, false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }
}
