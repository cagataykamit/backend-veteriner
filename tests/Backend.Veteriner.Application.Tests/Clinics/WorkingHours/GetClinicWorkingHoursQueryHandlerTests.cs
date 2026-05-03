using Backend.Veteriner.Application.Clinics.Queries.WorkingHours.GetClinicWorkingHours;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.WorkingHours;

public sealed class GetClinicWorkingHoursQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IReadRepository<ClinicWorkingHour>> _hoursRead = new();

    public GetClinicWorkingHoursQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _assignmentGuard
            .Setup(x => x.MustApplyAssignedClinicScopeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private GetClinicWorkingHoursQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _assignmentGuard.Object,
            _userClinics.Object,
            _clinicsRead.Object,
            _hoursRead.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId)
    {
        var c = new Clinic(tenantId, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(c, id);
        return c;
    }

    [Fact]
    public async Task Should_ReturnDefaultWeek_When_NoDbRows()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));
        _hoursRead.Setup(x => x.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());

        var result = await CreateHandler().Handle(new GetClinicWorkingHoursQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(7);
        result.Value.Should().BeEquivalentTo(ClinicWorkingHoursDefaults.BuildWeek(), opts => opts.WithStrictOrdering());
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

        var result = await CreateHandler().Handle(new GetClinicWorkingHoursQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Should_ReturnDbRows_When_Present()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));
        var row = ClinicWorkingHour.Create(tid, cid, DayOfWeek.Monday, false, new TimeOnly(10, 0), new TimeOnly(11, 0), null, null);
        _hoursRead.Setup(x => x.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour> { row });

        var result = await CreateHandler().Handle(new GetClinicWorkingHoursQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle();
        result.Value[0].DayOfWeek.Should().Be(DayOfWeek.Monday);
        result.Value[0].OpensAt.Should().Be(new TimeOnly(10, 0));
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
        _hoursRead.Setup(x => x.ListAsync(It.IsAny<ClinicWorkingHoursByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClinicWorkingHour>());

        var result = await CreateHandler().Handle(new GetClinicWorkingHoursQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(7);
    }
}
