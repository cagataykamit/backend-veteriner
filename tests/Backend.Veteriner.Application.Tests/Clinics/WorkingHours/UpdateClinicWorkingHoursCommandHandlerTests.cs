using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.WorkingHours;

public sealed class UpdateClinicWorkingHoursCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _hoursRead = new();
    private readonly Mock<IRepository<ClinicWorkingHour>> _hoursWrite = new();

    public UpdateClinicWorkingHoursCommandHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(true);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private UpdateClinicWorkingHoursCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _permissions.Object,
            _assignmentGuard.Object,
            _userClinics.Object,
            _clinicsRead.Object,
            _hoursRead.Object,
            _hoursWrite.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId)
    {
        var c = new Clinic(tenantId, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(c, id);
        return c;
    }

    [Fact]
    public async Task Should_ReturnAccessDenied_When_ClinicAdmin_NotAssigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(uid);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(uid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));

        var result = await CreateHandler().Handle(
            new UpdateClinicWorkingHoursCommand(cid, ClinicWorkingHoursDefaults.BuildWeek()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
        _hoursWrite.Verify(x => x.DeleteAsync(It.IsAny<ClinicWorkingHour>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_DeleteExisting_ThenInsertSeven()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));

        var old = ClinicWorkingHour.Create(tid, cid, DayOfWeek.Monday, false, new TimeOnly(8, 0), new TimeOnly(9, 0), null, null);
        var savedBucket = new List<ClinicWorkingHour>();
        _hoursWrite.Setup(x => x.AddAsync(It.IsAny<ClinicWorkingHour>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicWorkingHour, CancellationToken>((e, _) => savedBucket.Add(e));
        _hoursRead.SetupSequence(x => x.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour> { old })
            .ReturnsAsync(savedBucket);

        var week = ClinicWorkingHoursDefaults.BuildWeek();
        var result = await CreateHandler().Handle(new UpdateClinicWorkingHoursCommand(cid, week), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _hoursWrite.Verify(x => x.DeleteAsync(old, It.IsAny<CancellationToken>()), Times.Once);
        _hoursWrite.Verify(x => x.AddAsync(It.IsAny<ClinicWorkingHour>(), It.IsAny<CancellationToken>()), Times.Exactly(7));
        _hoursWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.Value!.Count.Should().Be(7);
    }

    [Fact]
    public async Task Should_Succeed_When_ClinicAdmin_Assigned()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(uid);
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(uid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));
        _hoursRead.SetupSequence(x => x.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>())
            .ReturnsAsync(new List<ClinicWorkingHour>());

        var result = await CreateHandler().Handle(
            new UpdateClinicWorkingHoursCommand(cid, ClinicWorkingHoursDefaults.BuildWeek()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
