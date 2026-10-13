using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Queries.GetSummary;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class GetPetSummaryQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private readonly List<Hospitalization> _hosp = [];
    private readonly List<Vaccination> _vaccinations = [];
    private readonly List<Examination> _exams = [];
    private readonly List<Visit> _visits = [];

    private Mock<IReadRepository<Hospitalization>> _hospRepo = default!;

    public GetPetSummaryQueryHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _permissions.Setup(p => p.HasPermission(It.IsAny<string>())).Returns(true);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPet());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }

    private static Pet NewPet()
    {
        var pet = new Pet(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat);
        pet.ApplyAlerts(["Allergy"], "Penisilin");
        return pet;
    }

    private GetPetSummaryQueryHandler CreateHandler(
        Mock<IClinicReadScopeResolver>? scope = null,
        Mock<IReadRepository<Hospitalization>>? hospRepo = null)
    {
        _hospRepo = hospRepo ?? InMemory(_hosp);
        return new(
            _tenant.Object,
            _clinicContext.Object,
            (scope ?? ClinicReadScopeResolverMock.Default()).Object,
            _permissions.Object,
            _pets.Object,
            _clinics.Object,
            _hospRepo.Object,
            InMemory(_vaccinations).Object,
            InMemory(_exams).Object,
            InMemory(_visits).Object,
            new FixedClock(),
            NullLogger<GetPetSummaryQueryHandler>.Instance);
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

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private Hospitalization AddHosp(DateTime admitted, bool discharged)
    {
        var h = new Hospitalization(_tenantId, _clinicId, _petId, null, admitted, null, "Gozlem", null);
        if (discharged)
            h.Discharge(admitted.AddDays(1), null);
        _hosp.Add(h);
        return h;
    }

    private Vaccination AddVaccination(VaccinationStatus status, DateTime? applied, DateTime? due, string name = "Karma")
    {
        var v = new Vaccination(_tenantId, _petId, _clinicId, null, Guid.NewGuid(), name, status, applied, due, null);
        _vaccinations.Add(v);
        return v;
    }

    private Examination AddExam(DateTime at, decimal? weight = null, DateTime? measuredAt = null, string reason = "Kontrol")
    {
        var e = new Examination(_tenantId, _clinicId, _petId, null, at, reason, null, null, null,
            weightKg: weight, vitalsMeasuredAtUtc: measuredAt);
        _exams.Add(e);
        return e;
    }

    private Visit AddVisit(DateTime at, bool voided = false)
    {
        var v = new Visit(_tenantId, _clinicId, _petId, null, null, Guid.NewGuid(), at);
        if (voided)
            v.MarkAsMistaken("Yanlis geldi", at.AddMinutes(1));
        _visits.Add(v);
        return v;
    }

    private Task<Domain.Shared.Result<PetSummaryDto>> Run(GetPetSummaryQueryHandler handler)
        => handler.Handle(new GetPetSummaryQuery(_petId), CancellationToken.None);

    [Fact]
    public async Task No_Records_Should_Give_Empty_Sections_And_Alerts()
    {
        var result = await Run(CreateHandler());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.PetAlerts.Flags.Should().Equal("Allergy");
        dto.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Empty);
        dto.Vaccinations.State.Should().Be(PetSummarySectionState.Empty);
        dto.LatestWeight.State.Should().Be(PetSummarySectionState.Empty);
        dto.LastVisit.State.Should().Be(PetSummarySectionState.Empty);
        dto.LastExamination.State.Should().Be(PetSummarySectionState.Empty);
        dto.ActiveHospitalization.Data.Should().BeNull();
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
    public async Task Active_Hospitalization_Is_Found_Beyond_The_Last_Ten_Records()
    {
        var active = AddHosp(Now.AddDays(-200), discharged: false);
        for (var i = 0; i < 11; i++)
            AddHosp(Now.AddDays(-100 + i), discharged: true);

        var result = await Run(CreateHandler());

        result.Value!.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Ok);
        result.Value.ActiveHospitalization.Data!.Id.Should().Be(active.Id);
    }

    [Fact]
    public async Task Only_Discharged_Hospitalizations_Should_Be_Empty()
    {
        AddHosp(Now.AddDays(-5), discharged: true);

        var result = await Run(CreateHandler());

        result.Value!.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Empty);
    }

    [Fact]
    public async Task Vaccinations_Separate_Last_Applied_From_Next_Due_And_Ignore_Cancelled()
    {
        AddVaccination(VaccinationStatus.Applied, Now.AddDays(-400), null, "Eski");
        var lastApplied = AddVaccination(VaccinationStatus.Applied, Now.AddDays(-30), null, "Son");
        AddVaccination(VaccinationStatus.Cancelled, null, Now.AddDays(5), "Iptal");
        AddVaccination(VaccinationStatus.Scheduled, null, Now.AddDays(60), "Uzak");
        var nextDue = AddVaccination(VaccinationStatus.Scheduled, null, Now.AddDays(10), "Yakin");

        var result = await Run(CreateHandler());

        var data = result.Value!.Vaccinations.Data!;
        result.Value.Vaccinations.State.Should().Be(PetSummarySectionState.Ok);
        data.LastApplied!.Id.Should().Be(lastApplied.Id);
        data.NextDue!.Id.Should().Be(nextDue.Id);
        data.NextDue.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task Overdue_Scheduled_Vaccination_Is_Flagged_And_Preferred_As_Earliest()
    {
        var overdue = AddVaccination(VaccinationStatus.Scheduled, null, Now.AddDays(-3));
        AddVaccination(VaccinationStatus.Scheduled, null, Now.AddDays(10));

        var result = await Run(CreateHandler());

        result.Value!.Vaccinations.Data!.NextDue!.Id.Should().Be(overdue.Id);
        result.Value.Vaccinations.Data.NextDue.IsOverdue.Should().BeTrue();
        result.Value.Vaccinations.Data.LastApplied.Should().BeNull();
    }

    [Fact]
    public async Task Only_Scheduled_Vaccination_Must_Not_Count_As_Applied()
    {
        AddVaccination(VaccinationStatus.Scheduled, null, Now.AddDays(10));

        var result = await Run(CreateHandler());

        result.Value!.Vaccinations.Data!.LastApplied.Should().BeNull();
    }

    [Fact]
    public async Task Latest_Weight_Uses_Vitals_Time_And_Skips_Exams_Without_Weight()
    {
        AddExam(Now.AddDays(-10), weight: 4.0m);
        var newest = AddExam(Now.AddDays(-20), weight: 4.4m, measuredAt: Now.AddDays(-2));
        AddExam(Now.AddDays(-1), weight: null);

        var result = await Run(CreateHandler());

        var w = result.Value!.LatestWeight.Data!;
        w.ExaminationId.Should().Be(newest.Id);
        w.WeightKg.Should().Be(4.4m);
        w.MeasuredAtUtc.Should().Be(Now.AddDays(-2));
        w.Source.Should().Be("Examination");
    }

    [Fact]
    public async Task Profile_Weight_Comes_From_Pet_Without_Date()
    {
        var pet = NewPet();
        pet.UpdateDetails(pet.Name, pet.SpeciesId, pet.Breed, pet.BirthDate, weight: 5.5m);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(pet);

        var result = await Run(CreateHandler());

        result.Value!.ProfileWeightKg.Should().Be(5.5m);
        result.Value.LatestWeight.State.Should().Be(PetSummarySectionState.Empty);
    }

    [Fact]
    public async Task Last_Visit_Ignores_Voided_And_Last_Examination_Is_Newest()
    {
        AddVisit(Now.AddDays(-5));
        AddVisit(Now.AddDays(-1), voided: true);
        AddExam(Now.AddDays(-9), reason: "Eski");
        var newestExam = AddExam(Now.AddDays(-3), reason: "Yeni");

        var result = await Run(CreateHandler());

        result.Value!.LastVisit.Data!.ArrivedAtUtc.Should().Be(Now.AddDays(-5));
        result.Value.LastExamination.Data!.Id.Should().Be(newestExam.Id);
    }

    [Fact]
    public async Task Section_Without_Permission_Is_Forbidden_And_Not_Queried_While_Others_Load()
    {
        AddHosp(Now.AddDays(-1), discharged: false);
        AddVisit(Now.AddDays(-1));
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Hospitalizations.Read)).Returns(false);

        var handler = CreateHandler();
        var result = await Run(handler);

        result.Value!.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.ActiveHospitalization.Data.Should().BeNull();
        result.Value.LastVisit.State.Should().Be(PetSummarySectionState.Ok);
        _hospRepo.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Hospitalization>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Examination_Permission_Missing_Forbids_Both_Weight_And_Last_Examination()
    {
        AddExam(Now.AddDays(-1), weight: 4m);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Examinations.Read)).Returns(false);

        var result = await Run(CreateHandler());

        result.Value!.LatestWeight.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.LastExamination.State.Should().Be(PetSummarySectionState.Forbidden);
    }

    [Fact]
    public async Task Pets_Read_Only_User_Still_Gets_Alerts_And_Profile_Weight()
    {
        _permissions.Reset();
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Pets.Read)).Returns(true);

        var result = await Run(CreateHandler());

        result.Value!.PetAlerts.Flags.Should().Equal("Allergy");
        result.Value.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.Vaccinations.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.LatestWeight.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.LastVisit.State.Should().Be(PetSummarySectionState.Forbidden);
        result.Value.LastExamination.State.Should().Be(PetSummarySectionState.Forbidden);
    }

    [Fact]
    public async Task Failing_Section_Is_Unavailable_And_Does_Not_Fail_The_Others()
    {
        AddVisit(Now.AddDays(-1));
        var failing = new Mock<IReadRepository<Hospitalization>>();
        failing.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Hospitalization>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Run(CreateHandler(hospRepo: failing));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Unavailable);
        result.Value.ActiveHospitalization.Data.Should().BeNull();
        result.Value.LastVisit.State.Should().Be(PetSummarySectionState.Ok);
    }

    [Fact]
    public async Task Cancellation_Is_Not_Swallowed_As_Unavailable()
    {
        using var cts = new CancellationTokenSource();
        var failing = new Mock<IReadRepository<Hospitalization>>();
        failing.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Hospitalization>>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var act = () => CreateHandler(hospRepo: failing).Handle(new GetPetSummaryQuery(_petId), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Clinic_Scope_Hides_Other_Clinics_Records()
    {
        var otherClinic = Guid.NewGuid();
        _hosp.Add(new Hospitalization(_tenantId, otherClinic, _petId, null, Now.AddDays(-1), null, "Baska klinik", null));
        AddVisit(Now.AddDays(-2));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await Run(CreateHandler(scope));

        result.Value!.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Empty);
        result.Value.LastVisit.State.Should().Be(PetSummarySectionState.Ok);
    }

    [Fact]
    public async Task No_Accessible_Clinics_Gives_Empty_Not_Forbidden()
    {
        AddVisit(Now.AddDays(-2));
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([]);

        var result = await Run(CreateHandler(scope));

        result.Value!.LastVisit.State.Should().Be(PetSummarySectionState.Empty);
        result.Value.ActiveHospitalization.State.Should().Be(PetSummarySectionState.Empty);
    }

    [Fact]
    public async Task Resolver_Failure_Is_Returned()
    {
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await CreateHandler(scope).Handle(
            new GetPetSummaryQuery(_petId, Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }
}
