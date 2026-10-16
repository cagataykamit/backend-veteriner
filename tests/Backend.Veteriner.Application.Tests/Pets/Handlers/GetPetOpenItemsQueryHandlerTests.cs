using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Queries.GetOpenItems;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class GetPetOpenItemsQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Pet _pet;

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private readonly List<Vaccination> _vaccinations = [];
    private readonly List<LabResult> _labs = [];
    private readonly List<ChargeSuggestion> _suggestions = [];
    private readonly List<Charge> _charges = [];
    private readonly List<Appointment> _appointments = [];

    private Mock<IReadRepository<Vaccination>> _vaccinationRepo = default!;

    public GetPetOpenItemsQueryHandlerTests()
    {
        _pet = new Pet(_tenantId, Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat);
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _permissions.Setup(p => p.HasPermission(It.IsAny<string>())).Returns(true);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_pet);
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }

    private GetPetOpenItemsQueryHandler CreateHandler(
        Mock<IClinicReadScopeResolver>? scope = null,
        Mock<IReadRepository<Vaccination>>? vaccinationRepo = null)
    {
        _vaccinationRepo = vaccinationRepo ?? InMemory(_vaccinations);
        return new(
            _tenant.Object,
            _clinicContext.Object,
            (scope ?? ClinicReadScopeResolverMock.Default()).Object,
            _permissions.Object,
            _pets.Object,
            _clinics.Object,
            _vaccinationRepo.Object,
            InMemory(_labs).Object,
            InMemoryProjected<ChargeSuggestion, PendingChargeSuggestionAmountRow>(_suggestions).Object,
            InMemory(_charges).Object,
            InMemory(_appointments).Object,
            new FixedClock(),
            NullLogger<GetPetOpenItemsQueryHandler>.Instance);
    }

    private static Mock<IReadRepository<T>> InMemory<T>(List<T> data) where T : class
    {
        var mock = new Mock<IReadRepository<T>>();
        mock.Setup(r => r.ListAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> s, CancellationToken _) => s.Evaluate(data).ToList());
        mock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> s, CancellationToken _) => s.Evaluate(data).FirstOrDefault());
        return mock;
    }

    private static Mock<IReadRepository<T>> InMemoryProjected<T, TResult>(List<T> data) where T : class
    {
        var mock = new Mock<IReadRepository<T>>();
        mock.Setup(r => r.ListAsync(It.IsAny<ISpecification<T, TResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T, TResult> s, CancellationToken _) => s.Evaluate(data).ToList());
        return mock;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private Vaccination AddVaccination(VaccinationStatus status, DateTime? due, Guid? petId = null)
    {
        var v = new Vaccination(_tenantId, petId ?? _pet.Id, _clinicId, null, Guid.NewGuid(), "Karma", status,
            status == VaccinationStatus.Applied ? Now.AddDays(-1) : null, due, null);
        _vaccinations.Add(v);
        return v;
    }

    private LabResult AddLab(bool resulted, string? interpretation = null, string name = "Hemogram")
    {
        var l = new LabResult(_tenantId, _clinicId, _pet.Id, null, null, null,
            resulted ? Now.AddDays(-1) : null, name, resulted ? "Normal" : null, interpretation, null);
        _labs.Add(l);
        return l;
    }

    private void AddSuggestion(decimal unitPrice, string currency, decimal quantity)
    {
        var exam = new Examination(_tenantId, _clinicId, _pet.Id, null, Now.AddHours(-1), "Kontrol", null, null, null);
        var product = new Product(_tenantId, "Pansuman", "Adet", unitPrice, currency, kind: ProductKind.Service);
        _suggestions.Add(ChargeSuggestion.ForExaminationService(exam, product, quantity, Now).Value!);
    }

    private Charge AddCharge(Guid? petId, decimal unitPrice, decimal allocated = 0)
    {
        var c = Charge.Create(_tenantId, _clinicId, _pet.ClientId, petId, null, null, "Muayene", 1, unitPrice,
            Guid.NewGuid(), Now.AddDays(-2), null).Value!;
        if (allocated > 0)
            c.ApplyAllocation(allocated);
        _charges.Add(c);
        return c;
    }

    private Task<Domain.Shared.Result<PetOpenItemsDto>> Run(GetPetOpenItemsQueryHandler handler)
        => handler.Handle(new GetPetOpenItemsQuery(_pet.Id), CancellationToken.None);

    [Fact]
    public async Task No_Records_Should_Give_Empty_Sections()
    {
        var result = await Run(CreateHandler());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.PetId.Should().Be(_pet.Id);
        dto.ClientId.Should().Be(_pet.ClientId);
        dto.Status.Should().Be(PetStatus.Active);
        dto.AsOfUtc.Should().Be(Now);
        dto.Vaccinations.State.Should().Be(PetSummarySectionState.Empty);
        dto.LabResults.State.Should().Be(PetSummarySectionState.Empty);
        dto.PendingCharges.State.Should().Be(PetSummarySectionState.Empty);
        dto.NextAppointment.State.Should().Be(PetSummarySectionState.Empty);
        dto.Outstanding.State.Should().Be(PetSummarySectionState.Empty);
    }

    [Fact]
    public async Task Unknown_Pet_Should_Return_NotFound()
    {
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await Run(CreateHandler());

        result.Error.Code.Should().Be("Pets.NotFound");
    }

    [Fact]
    public async Task Vaccinations_Split_Overdue_And_Upcoming_Within_30_Days()
    {
        var overdueOld = AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(-40));
        var overdue = AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(-1));
        var upcoming = AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(29));
        AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(31));
        AddVaccination(VaccinationStatus.Applied, null);
        AddVaccination(VaccinationStatus.Cancelled, Now.AddDays(-2));
        AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(-2), petId: Guid.NewGuid());

        var result = await Run(CreateHandler());

        var data = result.Value!.Vaccinations.Data!;
        result.Value.Vaccinations.State.Should().Be(PetSummarySectionState.Ok);
        data.Overdue.Select(v => v.Id).Should().Equal(overdueOld.Id, overdue.Id);
        data.Upcoming.Select(v => v.Id).Should().Equal(upcoming.Id);
    }

    // ADR-028: Now = İstanbul 2026-10-11 12:00; bugünün başı 2026-10-10 21:00 UTC, pencere sonu 2026-11-09 21:00 UTC (bugün dahil 30 gün).
    [Fact]
    public async Task Vaccinations_Split_Is_Day_Based()
    {
        var todayStartUtc = new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Utc);
        var yesterdayLate = AddVaccination(VaccinationStatus.Scheduled, todayStartUtc.AddMinutes(-1));
        var todayPastHour = AddVaccination(VaccinationStatus.Scheduled, Now.AddHours(-3));
        var lastWindowDay = AddVaccination(VaccinationStatus.Scheduled, todayStartUtc.AddDays(30).AddMinutes(-1));
        AddVaccination(VaccinationStatus.Scheduled, todayStartUtc.AddDays(30));

        var result = await Run(CreateHandler());

        var data = result.Value!.Vaccinations.Data!;
        data.Overdue.Select(v => v.Id).Should().Equal(yesterdayLate.Id);
        data.Upcoming.Select(v => v.Id).Should().Equal(todayPastHour.Id, lastWindowDay.Id);
    }

    [Fact]
    public async Task Deceased_Pet_Has_No_Vaccination_Items_And_Is_Not_Queried()
    {
        AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(-3));
        _pet.SetStatus(PetStatus.Deceased, null, DateOnly.FromDateTime(Now));

        var result = await Run(CreateHandler());

        result.Value!.Status.Should().Be(PetStatus.Deceased);
        result.Value.Vaccinations.State.Should().Be(PetSummarySectionState.Empty);
        _vaccinationRepo.Verify(
            r => r.ListAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deceased_Pet_Still_Shows_Next_Appointment_And_Other_Sections()
    {
        _pet.SetStatus(PetStatus.Deceased, null, DateOnly.FromDateTime(Now));
        _appointments.Add(new Appointment(_tenantId, _clinicId, _pet.Id, Now.AddDays(2)));
        AddLab(resulted: false);

        var result = await Run(CreateHandler());

        result.Value!.NextAppointment.State.Should().Be(PetSummarySectionState.Ok);
        result.Value.LabResults.State.Should().Be(PetSummarySectionState.Ok);
    }

    [Fact]
    public async Task Lab_Results_List_All_Awaiting_Result_And_Interpretation_Without_Ten_Limit()
    {
        var requested = Enumerable.Range(0, 12).Select(i => AddLab(resulted: false, name: $"Test {i}")).ToList();
        var awaitingInterpretation = AddLab(resulted: true);
        AddLab(resulted: true, interpretation: "Normal sınırlarda");
        AddLab(resulted: false).Cancel(Guid.NewGuid(), "Yanlış kayıt girildi");

        var result = await Run(CreateHandler());

        var data = result.Value!.LabResults.Data!;
        data.AwaitingResult.Should().HaveCount(12);
        data.AwaitingResult.Select(l => l.Id).Should().BeEquivalentTo(requested.Select(l => l.Id));
        data.AwaitingInterpretation.Select(l => l.Id).Should().Equal(awaitingInterpretation.Id);
    }

    [Fact]
    public async Task Pending_Charges_Give_Count_Total_And_Unpriced_Count()
    {
        AddSuggestion(150.50m, "TRY", 2);
        AddSuggestion(10m, "TRY", 0.333m);
        AddSuggestion(99m, "USD", 1);

        var result = await Run(CreateHandler());

        var data = result.Value!.PendingCharges.Data!;
        data.Count.Should().Be(3);
        data.TotalAmount.Should().Be(301.00m + 3.33m);
        data.UnpricedCount.Should().Be(1);
        data.Currency.Should().Be("TRY");
    }

    [Fact]
    public async Task Next_Appointment_Is_Earliest_Future_Scheduled_One()
    {
        _appointments.Add(new Appointment(_tenantId, _clinicId, _pet.Id, Now.AddHours(-2)));
        _appointments.Add(new Appointment(_tenantId, _clinicId, _pet.Id, Now.AddDays(1), initialStatus: AppointmentStatus.Cancelled));
        var next = new Appointment(_tenantId, _clinicId, _pet.Id, Now.AddDays(3), 45, AppointmentType.Vaccination);
        _appointments.Add(next);
        _appointments.Add(new Appointment(_tenantId, _clinicId, _pet.Id, Now.AddDays(10)));

        var result = await Run(CreateHandler());

        var data = result.Value!.NextAppointment.Data!;
        data.Id.Should().Be(next.Id);
        data.DurationMinutes.Should().Be(45);
        data.AppointmentType.Should().Be(AppointmentType.Vaccination);
    }

    [Fact]
    public async Task Outstanding_Separates_Pet_Share_From_Client_Total()
    {
        AddCharge(_pet.Id, 1000m, allocated: 400m);
        AddCharge(_pet.Id, 50m, allocated: 50m);
        AddCharge(Guid.NewGuid(), 200m);
        AddCharge(null, 30m);

        var result = await Run(CreateHandler());

        var data = result.Value!.Outstanding.Data!;
        data.PetOutstandingAmount.Should().Be(600m);
        data.PetOpenChargeCount.Should().Be(1);
        data.ClientOutstandingAmount.Should().Be(830m);
    }

    [Fact]
    public async Task Outstanding_Is_Ok_With_Zero_Pet_Share_When_Only_Client_Owes()
    {
        AddCharge(Guid.NewGuid(), 200m);

        var result = await Run(CreateHandler());

        result.Value!.Outstanding.State.Should().Be(PetSummarySectionState.Ok);
        result.Value.Outstanding.Data!.PetOutstandingAmount.Should().Be(0m);
        result.Value.Outstanding.Data.ClientOutstandingAmount.Should().Be(200m);
    }

    [Fact]
    public async Task Sections_Without_Permission_Are_Forbidden()
    {
        AddCharge(_pet.Id, 100m);
        AddVaccination(VaccinationStatus.Scheduled, Now.AddDays(-1));
        _permissions.Reset();
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Pets.Read)).Returns(true);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.LabResults.Read)).Returns(true);

        var result = await Run(CreateHandler());

        result.Value!.Vaccinations.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.PendingCharges.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.Outstanding.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.Outstanding.Data.Should().BeNull();
        result.Value.NextAppointment.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.LabResults.State.Should().Be(PetSummarySectionState.Empty);
        _vaccinationRepo.Verify(
            r => r.ListAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Failing_Section_Is_Unavailable_And_Does_Not_Fail_The_Others()
    {
        AddLab(resulted: false);
        var failing = new Mock<IReadRepository<Vaccination>>();
        failing.Setup(r => r.ListAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Run(CreateHandler(vaccinationRepo: failing));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Vaccinations.State.Should().Be(PetSummarySectionState.Unavailable);
        result.Value.LabResults.State.Should().Be(PetSummarySectionState.Ok);
    }

    [Fact]
    public async Task Clinic_Scope_Hides_Other_Clinics_Records()
    {
        var otherClinic = Guid.NewGuid();
        _vaccinations.Add(new Vaccination(_tenantId, _pet.Id, otherClinic, null, Guid.NewGuid(), "Kuduz",
            VaccinationStatus.Scheduled, null, Now.AddDays(-1), null));
        _charges.Add(Charge.Create(_tenantId, otherClinic, _pet.ClientId, _pet.Id, null, null, "Baska", 1, 100m,
            Guid.NewGuid(), Now, null).Value!);
        AddLab(resulted: false);
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await Run(CreateHandler(scope));

        result.Value!.Vaccinations.State.Should().Be(PetSummarySectionState.Empty);
        result.Value.Outstanding.State.Should().Be(PetSummarySectionState.Empty);
        result.Value.LabResults.State.Should().Be(PetSummarySectionState.Ok);
    }

    [Fact]
    public async Task No_Accessible_Clinics_Gives_Empty_Not_Forbidden()
    {
        AddLab(resulted: false);
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([]);

        var result = await Run(CreateHandler(scope));

        result.Value!.LabResults.State.Should().Be(PetSummarySectionState.Empty);
        result.Value.Outstanding.State.Should().Be(PetSummarySectionState.Empty);
    }

    [Fact]
    public async Task Resolver_Failure_Is_Returned()
    {
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await CreateHandler(scope).Handle(
            new GetPetOpenItemsQuery(_pet.Id, Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }
}
