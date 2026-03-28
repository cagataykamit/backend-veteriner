using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Commands.Update;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Handlers;

public sealed class UpdateVaccinationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinationsRead = new();
    private readonly Mock<IRepository<Vaccination>> _vaccinationsWrite = new();

    private UpdateVaccinationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _vaccinationsRead.Object,
            _vaccinationsWrite.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_VaccinationNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Vaccination?)null);

        var cmd = new UpdateVaccinationCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(3),
            null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.NotFound");
        _vaccinationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Vaccination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_RequestClinic_Differs_From_ActiveClinicContext()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var cmd = new UpdateVaccinationCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Kuduz",
            VaccinationStatus.Scheduled,
            null,
            DateTime.UtcNow.AddDays(3),
            null);

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Vaccinations.ClinicContextMismatch");
        _vaccinationsRead.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Update_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var vid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null));

        var due = DateTime.UtcNow.AddDays(5);
        var existing = new Vaccination(
            tid,
            pid,
            cid,
            null,
            "Eski",
            VaccinationStatus.Scheduled,
            null,
            due,
            null);
        typeof(Vaccination).GetProperty(nameof(Vaccination.Id))!.SetValue(existing, vid);

        _vaccinationsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VaccinationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var cmd = new UpdateVaccinationCommand(
            vid,
            cid,
            pid,
            null,
            "Karma aşı",
            VaccinationStatus.Scheduled,
            null,
            due,
            "not");

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.VaccineName.Should().Be("Karma aşı");
        existing.Notes.Should().Be("not");
        _vaccinationsWrite.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _vaccinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
