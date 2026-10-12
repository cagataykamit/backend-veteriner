using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Queries.GetTimeline;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Pets.Timeline;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Timeline;

public sealed class PetTimelineHandlerTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private readonly List<Visit> _visits = [];
    private readonly List<Examination> _exams = [];
    private readonly List<Treatment> _treatments = [];
    private readonly List<Prescription> _rx = [];
    private readonly List<LabResult> _labs = [];
    private readonly List<Vaccination> _vaccinations = [];
    private readonly List<Hospitalization> _hosp = [];

    public PetTimelineHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _permissions.Setup(p => p.HasPermission(It.IsAny<string>())).Returns(true);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(_tenantId, Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat));
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }

    private GetPetTimelineQueryHandler CreateHandler(Mock<IClinicReadScopeResolver>? scope = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scope ?? ClinicReadScopeResolverMock.Default()).Object,
            _permissions.Object,
            _pets.Object,
            _clinics.Object,
            InMemory(_exams).Object,
            [
                new VisitTimelineSource(InMemory(_visits).Object),
                new ExaminationTimelineSource(InMemory(_exams).Object),
                new TreatmentTimelineSource(InMemory(_treatments).Object),
                new PrescriptionTimelineSource(InMemory(_rx).Object),
                new LabResultTimelineSource(InMemory(_labs).Object),
                new VaccinationTimelineSource(InMemory(_vaccinations).Object),
                new HospitalizationTimelineSource(InMemory(_hosp).Object)
            ]);

    /// <summary>Spec'i bellekte değerlendirir (EF'e çeviri entegrasyon testinde doğrulanır).</summary>
    private static Mock<IReadRepository<T>> InMemory<T>(List<T> data) where T : class
    {
        var mock = new Mock<IReadRepository<T>>();
        mock.Setup(r => r.ListAsync(It.IsAny<ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<T> spec, CancellationToken _) => spec.Evaluate(data).ToList());
        return mock;
    }

    private Visit NewVisit(DateTime at, bool voided = false, Guid? clinicId = null, Guid? appointmentId = null)
    {
        var v = new Visit(_tenantId, clinicId ?? _clinicId, _petId, appointmentId, null, Guid.NewGuid(), at);
        if (voided)
            v.MarkAsMistaken("Yanlis geldi", at.AddMinutes(1));
        _visits.Add(v);
        return v;
    }

    private Examination NewExam(DateTime at, Guid? visitId = null, Guid? clinicId = null)
    {
        var e = new Examination(_tenantId, clinicId ?? _clinicId, _petId, null, at, "Kontrol", null, null, null, visitId: visitId);
        _exams.Add(e);
        return e;
    }

    private Vaccination NewVaccination(VaccinationStatus status, DateTime? applied, DateTime? due)
    {
        var v = new Vaccination(_tenantId, _petId, _clinicId, null, Guid.NewGuid(), "Karma", status, applied, due, null);
        _vaccinations.Add(v);
        return v;
    }

    private Treatment NewTreatment(DateTime at, Guid? examinationId = null)
    {
        var t = new Treatment(_tenantId, _clinicId, _petId, examinationId, at, "Serum", "Aciklama", null, null);
        _treatments.Add(t);
        return t;
    }

    private Hospitalization NewHospitalization(DateTime at)
    {
        var h = new Hospitalization(_tenantId, _clinicId, _petId, null, at, null, "Gozlem", null);
        _hosp.Add(h);
        return h;
    }

    // ---- Doğrulama ----

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-3)]
    public async Task Invalid_Limit_Should_Fail_Validation(int limit)
    {
        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, Limit: limit), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.Validation");
    }

    [Theory]
    [InlineData("Payment")]
    [InlineData("Appointment")]
    [InlineData("Bilinmeyen")]
    public async Task Unsupported_Or_Unknown_Type_Should_Fail_Validation(string type)
    {
        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, Types: [type]), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.Validation");
    }

    [Fact]
    public async Task Garbage_Cursor_Should_Fail_Validation()
    {
        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, Cursor: "!!!"), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.Validation");
    }

    [Fact]
    public async Task FromUtc_Not_Before_ToUtc_Should_Fail_Validation()
    {
        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, FromUtc: T0, ToUtc: T0), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.Validation");
    }

    [Fact]
    public async Task Unknown_Pet_Should_Return_NotFound()
    {
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.NotFound");
    }

    // ---- İzin süzmesi ----

    [Fact]
    public async Task Types_Without_Permission_Should_Be_Omitted_And_Not_Queried()
    {
        NewVisit(T0);
        NewTreatment(T0.AddHours(1));
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Treatments.Read)).Returns(false);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OmittedTypes.Should().Equal("Treatment");
        result.Value.Items.Select(i => i.Type).Should().Equal(PetRecordKind.Visit);
    }

    [Fact]
    public async Task Requested_Type_Without_Permission_Should_Appear_In_OmittedTypes_With_Empty_Items()
    {
        NewTreatment(T0);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Treatments.Read)).Returns(false);

        var result = await CreateHandler().Handle(
            new GetPetTimelineQuery(_petId, Types: ["Treatment"]), CancellationToken.None);

        result.Value!.Items.Should().BeEmpty();
        result.Value.OmittedTypes.Should().Equal("Treatment");
    }

    [Fact]
    public async Task Empty_Result_With_Permission_Should_Not_Report_Omitted()
    {
        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Should().BeEmpty();
        result.Value.OmittedTypes.Should().BeEmpty();
        result.Value.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Pets_Read_Only_User_Should_See_Nothing_But_All_Types_Omitted()
    {
        NewVisit(T0);
        _permissions.Reset();
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Pets.Read)).Returns(true);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Should().BeEmpty();
        result.Value.OmittedTypes.Should().BeEquivalentTo(PetTimelineConstants.SupportedKinds.Select(k => k.ToString()));
    }

    // ---- Klinik kapsamı ----

    [Fact]
    public async Task Clinic_Scope_Should_Hide_Other_Clinics_Records()
    {
        var other = Guid.NewGuid();
        NewVisit(T0);
        NewVisit(T0.AddHours(1), clinicId: other);
        NewExam(T0.AddHours(2), clinicId: other);
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await CreateHandler(scope).Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.ClinicId.Should().Be(_clinicId);
    }

    [Fact]
    public async Task No_Accessible_Clinics_Should_Return_Empty_Without_Omitting()
    {
        NewVisit(T0);
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([]);

        var result = await CreateHandler(scope).Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Should().BeEmpty();
        result.Value.OmittedTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task Inaccessible_ClinicId_Parameter_Should_Fail_With_Resolver_Error()
    {
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await CreateHandler(scope).Handle(
            new GetPetTimelineQuery(_petId, ClinicId: Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    // ---- Geliş satırında bağlı muayene ----

    [Fact]
    public async Task Visit_Row_Should_Link_Latest_Linked_Examination()
    {
        var visit = NewVisit(T0);
        NewExam(T0.AddMinutes(10), visitId: visit.Id);
        var latest = NewExam(T0.AddMinutes(30), visitId: visit.Id);
        NewExam(T0.AddMinutes(20), visitId: visit.Id);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Single(i => i.Type == PetRecordKind.Visit).RelatedExaminationId.Should().Be(latest.Id);
    }

    [Fact]
    public async Task Visit_Row_Without_Linked_Examination_Should_Have_Null()
    {
        NewVisit(T0);
        NewExam(T0.AddMinutes(10));

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Single(i => i.Type == PetRecordKind.Visit).RelatedExaminationId.Should().BeNull();
    }

    [Fact]
    public async Task Visit_Row_Should_Link_Examination_Even_When_Only_Visit_Type_Requested()
    {
        var visit = NewVisit(T0);
        var exam = NewExam(T0.AddMinutes(10), visitId: visit.Id);

        var result = await CreateHandler().Handle(
            new GetPetTimelineQuery(_petId, Types: ["Visit"]), CancellationToken.None);

        result.Value!.Items.Should().ContainSingle().Which.RelatedExaminationId.Should().Be(exam.Id);
    }

    [Fact]
    public async Task Visit_Row_Should_Not_Expose_Examination_Without_Examinations_Read()
    {
        var visit = NewVisit(T0);
        NewExam(T0.AddMinutes(10), visitId: visit.Id);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Examinations.Read)).Returns(false);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.OmittedTypes.Should().Equal("Examination");
        result.Value.Items.Should().ContainSingle().Which.RelatedExaminationId.Should().BeNull();
    }

    [Fact]
    public async Task Visit_Row_Should_Ignore_Examination_Outside_Clinic_Scope()
    {
        var otherClinic = Guid.NewGuid();
        var visit = NewVisit(T0);
        NewExam(T0.AddMinutes(10), visitId: visit.Id, clinicId: otherClinic);
        var scope = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        var result = await CreateHandler(scope).Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Single(i => i.Type == PetRecordKind.Visit).RelatedExaminationId.Should().BeNull();
    }

    // ---- Kapsam: iptal/yanlış işaretli, planlı aşı, randevu katlama ----

    [Fact]
    public async Task Voided_Visit_And_Cancelled_Vaccination_Are_Hidden_By_Default_And_Shown_With_Flag()
    {
        NewVisit(T0, voided: true);
        NewVaccination(VaccinationStatus.Cancelled, null, T0.AddDays(1));
        NewVaccination(VaccinationStatus.Applied, T0.AddDays(2), null);

        var hidden = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);
        var shown = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, IncludeVoided: true), CancellationToken.None);

        hidden.Value!.Items.Should().ContainSingle().Which.IsVoided.Should().BeFalse();
        shown.Value!.Items.Should().HaveCount(3);
        shown.Value.Items.Count(i => i.IsVoided).Should().Be(2);
    }

[Fact]
    public async Task LabResult_Should_Be_Single_Row_At_RequestTime_With_Status_And_Cancelled_Hidden_By_Default()
    {
        var requested = new LabResult(_tenantId, _clinicId, _petId, null, null, null, null, "Kan sayımı", null, null, null);
        var resulted = new LabResult(_tenantId, _clinicId, _petId, null, Guid.NewGuid(), null, T0, "Biyokimya", "Normal", null, null);
        var cancelled = new LabResult(_tenantId, _clinicId, _petId, null, null, null, null, "İdrar", null, null, null);
        cancelled.Cancel(Guid.NewGuid(), "Yanlış hasta için istendi").IsSuccess.Should().BeTrue();
        _labs.AddRange([requested, resulted, cancelled]);

        var hidden = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);
        var shown = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, IncludeVoided: true), CancellationToken.None);

        hidden.Value!.Items.Select(i => i.Status).Should().BeEquivalentTo(["Requested", "Resulted"]);
        hidden.Value.Items.Single(i => i.Status == "Requested").EventAtUtc.Should().Be(requested.RequestedAtUtc);
        hidden.Value.Items.Single(i => i.Status == "Resulted").RelatedVisitId.Should().Be(resulted.VisitId);
        shown.Value!.Items.Should().HaveCount(3);
        shown.Value.Items.Single(i => i.Status == "Cancelled").IsVoided.Should().BeTrue();
    }

    [Fact]
    public async Task Scheduled_Vaccination_Should_Never_Be_On_Timeline()
    {
        NewVaccination(VaccinationStatus.Scheduled, null, T0.AddDays(5));

        var result = await CreateHandler().Handle(
            new GetPetTimelineQuery(_petId, IncludeVoided: true), CancellationToken.None);

        result.Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Appointment_Visit_Is_Single_Row_With_Appointment_Link()
    {
        var appointmentId = Guid.NewGuid();
        var visit = NewVisit(T0, appointmentId: appointmentId);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.SourceId.Should().Be(visit.Id);
        item.Type.Should().Be(PetRecordKind.Visit);
        item.RelatedAppointmentId.Should().Be(appointmentId);
        item.LinkRef.Id.Should().Be(visit.Id);
    }

    [Fact]
    public async Task Hospitalization_Is_One_Event_At_Admission_With_Status()
    {
        var h = NewHospitalization(T0);
        _hosp.Single().Discharge(T0.AddDays(2), false, null);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.EventAtUtc.Should().Be(T0);
        item.Status.Should().Be("Discharged");
        item.SourceId.Should().Be(h.Id);
    }

    [Fact]
    public async Task Related_Ids_Are_Carried_On_Rows()
    {
        var visit = NewVisit(T0);
        var exam = NewExam(T0.AddHours(1), visitId: visit.Id);
        NewTreatment(T0.AddHours(2), examinationId: exam.Id);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Single(i => i.Type == PetRecordKind.Examination).RelatedVisitId.Should().Be(visit.Id);
        result.Value.Items.Single(i => i.Type == PetRecordKind.Treatment).RelatedExaminationId.Should().Be(exam.Id);
    }

    [Fact]
    public async Task Date_Range_Is_Half_Open()
    {
        NewVisit(T0);
        NewVisit(T0.AddDays(1));
        NewVisit(T0.AddDays(2));

        var result = await CreateHandler().Handle(
            new GetPetTimelineQuery(_petId, FromUtc: T0, ToUtc: T0.AddDays(2)), CancellationToken.None);

        result.Value!.Items.Select(i => i.EventAtUtc).Should().Equal(T0.AddDays(1), T0);
    }

    // ---- Sıralama ve sayfalama ----

    [Fact]
    public async Task Pagination_Walks_All_Rows_In_Total_Order_Without_Gaps_Or_Duplicates_Including_Ties()
    {
        // Aynı anda farklı türler + aynı türden aynı anda birden çok kayıt (id ile ayrışır).
        for (var i = 0; i < 4; i++)
        {
            NewVisit(T0);
            NewExam(T0);
            NewTreatment(T0);
        }
        for (var day = 1; day <= 5; day++)
        {
            NewVisit(T0.AddDays(day));
            NewExam(T0.AddDays(day));
            NewHospitalization(T0.AddDays(day));
            NewVaccination(VaccinationStatus.Applied, T0.AddDays(day), null);
        }

        var all = _visits.Select(x => (x.ArrivedAtUtc, Kind: PetRecordKind.Visit, x.Id))
            .Concat(_exams.Select(x => (x.ExaminedAtUtc, Kind: PetRecordKind.Examination, x.Id)))
            .Concat(_treatments.Select(x => (x.TreatmentDateUtc, Kind: PetRecordKind.Treatment, x.Id)))
            .Concat(_hosp.Select(x => (x.AdmittedAtUtc, Kind: PetRecordKind.Hospitalization, x.Id)))
            .Concat(_vaccinations.Select(x => (AppliedAtUtc: x.AppliedAtUtc!.Value, Kind: PetRecordKind.Vaccination, x.Id)))
            .OrderByDescending(x => x.Item1).ThenByDescending(x => (int)x.Kind).ThenByDescending(x => x.Id)
            .Select(x => x.Id)
            .ToList();

        var walked = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var result = await CreateHandler().Handle(
                new GetPetTimelineQuery(_petId, Limit: 7, Cursor: cursor), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            walked.AddRange(result.Value!.Items.Select(i => i.SourceId));
            cursor = result.Value.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 50);

        walked.Should().Equal(all);
        walked.Should().OnlyHaveUniqueItems();
        pages.Should().Be((all.Count + 6) / 7);
    }

    [Fact]
    public async Task Last_Page_Has_No_NextCursor_When_Exactly_Limit_Rows_Remain()
    {
        for (var i = 0; i < 4; i++)
            NewVisit(T0.AddHours(i));

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, Limit: 4), CancellationToken.None);

        result.Value!.Items.Should().HaveCount(4);
        result.Value.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Newer_Insert_Between_Pages_Does_Not_Break_Cursor()
    {
        for (var i = 0; i < 6; i++)
            NewVisit(T0.AddHours(i));

        var first = await CreateHandler().Handle(new GetPetTimelineQuery(_petId, Limit: 3), CancellationToken.None);
        NewVisit(T0.AddDays(10));
        var second = await CreateHandler().Handle(
            new GetPetTimelineQuery(_petId, Limit: 3, Cursor: first.Value!.NextCursor), CancellationToken.None);

        second.Value!.Items.Select(i => i.EventAtUtc).Should().Equal(T0.AddHours(2), T0.AddHours(1), T0);
    }

    [Fact]
    public async Task Clinic_Name_Is_Resolved()
    {
        NewVisit(T0);
        var clinic = new Clinic(_tenantId, "Merkez Klinik", "Ankara");
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { clinic });
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, _clinicId);

        var result = await CreateHandler().Handle(new GetPetTimelineQuery(_petId), CancellationToken.None);

        result.Value!.Items.Single().ClinicName.Should().Be("Merkez Klinik");
    }
}

public sealed class PetTimelineCursorTests
{
    [Fact]
    public void Encode_Decode_RoundTrips()
    {
        var cursor = new PetTimelineCursor(new DateTime(2026, 10, 11, 8, 30, 15, 123, DateTimeKind.Utc).AddTicks(4567), 6, Guid.NewGuid());

        PetTimelineCursor.TryDecode(cursor.Encode(), out var decoded).Should().BeTrue();

        decoded.Should().Be(cursor);
        decoded!.EventAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%%%")]
    [InlineData("YWJj")]
    [InlineData("MS4yLjM")]
    public void Garbage_Does_Not_Decode(string? value)
    {
        PetTimelineCursor.TryDecode(value, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    public void Out_Of_Range_Ticks_Do_Not_Decode()
    {
        var raw = System.Text.Encoding.ASCII.GetBytes($"{long.MaxValue}.1.{Guid.NewGuid():N}");
        var value = Convert.ToBase64String(raw).TrimEnd('=');

        PetTimelineCursor.TryDecode(value, out _).Should().BeFalse();
    }
}

public sealed class PetRecordPermissionsTests
{
    [Fact]
    public void Split_Preserves_Order_And_Separates_By_Permission()
    {
        var checker = new Mock<ICurrentUserPermissionChecker>();
        checker.Setup(c => c.HasPermission(PermissionCatalog.Visits.Read)).Returns(true);
        checker.Setup(c => c.HasPermission(PermissionCatalog.Payments.Read)).Returns(false);
        checker.Setup(c => c.HasPermission(PermissionCatalog.LabResults.Read)).Returns(true);

        var (allowed, omitted) = checker.Object.Split([PetRecordKind.LabResult, PetRecordKind.Payment, PetRecordKind.Visit]);

        allowed.Should().Equal(PetRecordKind.LabResult, PetRecordKind.Visit);
        omitted.Should().Equal(PetRecordKind.Payment);
    }

    [Fact]
    public void Every_Kind_Maps_To_A_Read_Permission()
    {
        foreach (var kind in Enum.GetValues<PetRecordKind>())
            PetRecordPermissions.RequiredPermission(kind).Should().EndWith(".Read");
    }

}
