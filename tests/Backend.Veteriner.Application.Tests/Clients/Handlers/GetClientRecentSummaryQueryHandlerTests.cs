using Backend.Veteriner.Application.Clients.Queries.GetRecentSummary;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class GetClientRecentSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();

    private GetClientRecentSummaryQueryHandler CreateHandler(Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _clients.Object,
            _pets.Object,
            _appointments.Object,
            _examinations.Object);

    private void SetupClientAndPet(Guid tenantId, Guid clientId, Guid petId)
    {
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tenantId, "Ali", "05321111111"));
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantClientIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { new(tenantId, clientId, "Pamuk", TestSpeciesIds.Cat) });
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Examination>());
    }

    [Fact]
    public async Task Handle_TenantWideAdmin_NoActiveClinic_Should_LoadRecentData()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClientAndPet(tid, clientId, Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetClientRecentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NonTenantWide_WithAssignedClinics_Should_LoadScopedRecentData()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, new[] { c1 })));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupClientAndPet(tid, clientId, Guid.NewGuid());

        var result = await CreateHandler(scope).Handle(new GetClientRecentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoAssignments_Should_ReturnEmptySummary()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Success(new ClinicReadScope(null, Array.Empty<Guid>())));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", "05321111111"));

        var result = await CreateHandler(scope).Handle(new GetClientRecentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecentAppointments.Should().BeEmpty();
        result.Value.RecentExaminations.Should().BeEmpty();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActiveAssignedClinic_Should_UseSingleClinicScope()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { cid });
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupClientAndPet(tid, clientId, Guid.NewGuid());

        var result = await CreateHandler(scope).Handle(new GetClientRecentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _examinations.Verify(
            r => r.ListAsync(It.IsAny<ExaminationsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ResolverFailure_Should_Fail_WithoutRepositoryCalls()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new Mock<IClinicReadScopeResolver>();
        scope.Setup(r => r.ResolveAsync(tid, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClinicReadScope>.Failure("Clinics.AccessDenied", "denied"));
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", "05321111111"));

        var result = await CreateHandler(scope).Handle(new GetClientRecentSummaryQuery(clientId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _appointments.Verify(
            r => r.ListAsync(It.IsAny<AppointmentsForClientPetsRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
