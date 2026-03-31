using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class UpdateExaminationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Examination>> _examinationsRead = new();
    private readonly Mock<IRepository<Examination>> _examinationsWrite = new();

    private UpdateExaminationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointments.Object,
            _examinationsRead.Object,
            _examinationsWrite.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_RouteEntityNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Examination?)null);

        var cmd = new UpdateExaminationCommand(
            Guid.NewGuid(),
            ClinicId: Guid.NewGuid(),
            PetId: Guid.NewGuid(),
            AppointmentId: null,
            ExaminedAtUtc: DateTime.UtcNow,
            VisitReason: "Sikayet",
            Findings: "Bulgu",
            Assessment: null,
            Notes: null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Update_When_ValidWithAppointmentOnly()
    {
        var tid = Guid.NewGuid();
        var eid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var aid = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = new Examination(tid, Guid.NewGuid(), Guid.NewGuid(), null, DateTime.UtcNow.AddHours(-1), "Old", "Old", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(existing, eid);

        _examinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Appointment(tid, cid, pid, DateTime.UtcNow.AddDays(1), AppointmentType.Other, null, null));

        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "Istanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var cmd = new UpdateExaminationCommand(
            eid,
            ClinicId: null,
            PetId: null,
            AppointmentId: aid,
            ExaminedAtUtc: DateTime.UtcNow,
            VisitReason: "Sikayet",
            Findings: "Bulgu",
            Assessment: "A",
            Notes: null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.AppointmentId.Should().Be(aid);
        existing.ClinicId.Should().Be(cid);
        existing.PetId.Should().Be(pid);
        existing.VisitReason.Should().Be("Sikayet");
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

