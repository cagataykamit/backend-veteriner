using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Queries.GetHistorySummary;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class GetPetHistorySummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptions = new();
    private readonly Mock<IReadRepository<LabResult>> _labResults = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();

    private GetPetHistorySummaryQueryHandler CreateHandler(Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _appointments.Object,
            _examinations.Object,
            _treatments.Object,
            _prescriptions.Object,
            _labResults.Object,
            _hospitalizations.Object,
            _payments.Object);

    private void SetupEmptyHistoryRepos()
    {
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination>());
        _treatments.Setup(r => r.ListAsync(It.IsAny<TreatmentsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Treatment>());
        _prescriptions.Setup(r => r.ListAsync(It.IsAny<PrescriptionsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        _labResults.Setup(r => r.ListAsync(It.IsAny<LabResultsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LabResult>());
        _hospitalizations.Setup(r => r.ListAsync(It.IsAny<HospitalizationsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Hospitalization>());
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsForPetRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
    }

    private void SetupPet(Guid tenantId, Guid clientId, Guid petId)
    {
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tenantId, clientId, "Pamuk", TestSpeciesIds.Cat));
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tenantId, "Ali", "05321111111"));
        SetupEmptyHistoryRepos();
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_NoActiveClinic_Should_LoadHistory()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupPet(tid, clientId, petId);

        var result = await CreateHandler().Handle(new GetPetHistorySummaryQuery(petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForPetRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonTenantWide_WithAssignedClinics_Should_LoadScopedHistory()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1 })));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupPet(tid, clientId, petId);

        var result = await CreateHandler(scope).Handle(new GetPetHistorySummaryQuery(petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForPetRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoAssignments_Should_ReturnEmptyHistory()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, Array.Empty<Guid>())));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupPet(tid, clientId, petId);

        var result = await CreateHandler(scope).Handle(new GetPetHistorySummaryQuery(petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecentAppointments.Should().BeEmpty();
        result.Value.RecentPayments.Should().BeEmpty();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForPetRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveAssignedClinic_Should_UseSingleClinicScope()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupPet(tid, clientId, petId);

        var result = await CreateHandler(scope).Handle(new GetPetHistorySummaryQuery(petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _examinations.Verify(
            r => r.ListAsync(It.IsAny<ExaminationsForPetRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ResolverFailure_Should_Fail_WithoutRepositoryCalls()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure("Clinics.AccessDenied", "denied"));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupPet(tid, clientId, petId);

        var result = await CreateHandler(scope).Handle(new GetPetHistorySummaryQuery(petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForPetRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
