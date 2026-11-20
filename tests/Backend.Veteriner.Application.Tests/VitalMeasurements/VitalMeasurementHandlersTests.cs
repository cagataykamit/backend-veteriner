using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.VitalMeasurements;
using Backend.Veteriner.Application.VitalMeasurements.Commands.Cancel;
using Backend.Veteriner.Application.VitalMeasurements.Commands.Create;
using Backend.Veteriner.Application.VitalMeasurements.Commands.Update;
using Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;
using Backend.Veteriner.Application.VitalMeasurements.Queries.GetById;
using Backend.Veteriner.Application.VitalMeasurements.Queries.GetSeries;
using Backend.Veteriner.Application.VitalMeasurements.Specs;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.VitalMeasurements;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.VitalMeasurements;

/// <summary>HOSP-004: bağ kuralları, zaman penceresi, geç kayıt, eşzamanlılık, kapsam ve eğilim birleştirme.</summary>
public sealed class VitalMeasurementHandlersTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Version = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IRepository<VitalMeasurement>> _write = new();
    private readonly Mock<IReadRepository<VitalMeasurement>> _read = new();

    public VitalMeasurementHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _permissions.Setup(p => p.HasPermission(It.IsAny<string>())).Returns(true);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A", Now));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(_tenantId, "K", "İstanbul"));
        var pet = new Pet(_tenantId, Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, _petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(pet);
        _write.Setup(r => r.AddAsync(It.IsAny<VitalMeasurement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VitalMeasurement m, CancellationToken _) => m);
        _write.Setup(r => r.UpdateAsync(It.IsAny<VitalMeasurement>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _write.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreateVitalMeasurementCommandHandler CreateHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _clientContext.Object, _scopeResolver.Object, _tenants.Object,
            _clinics.Object, _pets.Object, _hospitalizations.Object, _examinations.Object, _write.Object, _time);

    private UpdateVitalMeasurementCommandHandler UpdateHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _tenants.Object, _hospitalizations.Object,
            _examinations.Object, _write.Object, _time);

    private CancelVitalMeasurementCommandHandler CancelHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _clientContext.Object, _scopeResolver.Object, _tenants.Object, _write.Object, _time);

    private GetVitalSeriesQueryHandler SeriesHandler()
        => new(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _permissions.Object, _pets.Object,
            _hospitalizations.Object, _read.Object, _examinations.Object);

    private CreateVitalMeasurementCommand Cmd(
        Guid? hospitalizationId = null, Guid? examinationId = null, DateTime? at = null, decimal? temperature = 38.6m, string? lateReason = null)
        => new(_clinicId, _petId, hospitalizationId, examinationId, at ?? Now.AddMinutes(-30), temperature, 120, 24, null, null, lateReason);

    private Hospitalization Stay(DateTime? admittedAt = null, Guid? petId = null, Guid? clinicId = null)
    {
        var h = new Hospitalization(_tenantId, clinicId ?? _clinicId, petId ?? _petId, null, admittedAt ?? Now.AddDays(-1), null, "Gözlem", null, Now);
        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(h);
        return h;
    }

    private Examination Exam(bool finalized = false, Guid? petId = null)
    {
        var e = new Examination(_tenantId, _clinicId, petId ?? _petId, null, Now.AddHours(-2), "Kontrol", "Bulgu", null, null, Now);
        if (finalized)
            e.Finalize(Guid.NewGuid(), Now).IsSuccess.Should().BeTrue();
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(e);
        return e;
    }

    private VitalMeasurement Existing(Guid? hospitalizationId = null, Guid? examinationId = null, Guid? clinicId = null)
    {
        var m = VitalMeasurement.Create(
            _tenantId, clinicId ?? _clinicId, _petId, hospitalizationId, examinationId, Now.AddHours(-1),
            VitalSigns.Create(38.6m, null, null, null).Value!, null, _userId, Now).Value!;
        m.SetExpectedRowVersion(Convert.FromBase64String(Version));
        _write.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VitalMeasurementByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(m);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VitalMeasurementByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(m);
        return m;
    }

    [Fact]
    public async Task Create_Should_Persist_Standalone_Measurement_With_Author()
    {
        VitalMeasurement? added = null;
        _write.Setup(r => r.AddAsync(It.IsAny<VitalMeasurement>(), It.IsAny<CancellationToken>()))
            .Callback<VitalMeasurement, CancellationToken>((m, _) => added = m)
            .ReturnsAsync((VitalMeasurement m, CancellationToken _) => m);

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.CreatedByUserId.Should().Be(_userId);
        added.ClinicId.Should().Be(_clinicId);
        added.TemperatureC.Should().Be(38.6m);
        added.HeartRateBpm.Should().Be(120);
    }

    [Fact]
    public async Task Create_Should_Reject_Values_Window_And_Future()
    {
        (await CreateHandler().Handle(Cmd(temperature: 46m), CancellationToken.None)).Error.Code.Should().Be(VitalSigns.ValidationCode);
        (await CreateHandler().Handle(Cmd(at: Now.AddDays(-8)), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.MeasuredAtTooFarInPast");
        (await CreateHandler().Handle(Cmd(at: Now.AddMinutes(10)), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.MeasuredAtInFuture");
        (await CreateHandler().Handle(Cmd(at: Now.AddMinutes(4)), CancellationToken.None)).IsSuccess.Should().BeTrue("saat kayması payı");
        _write.Verify(r => r.AddAsync(It.IsAny<VitalMeasurement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_On_Hospitalization_Should_Check_Pet_Clinic_Cancel_And_Stay_Window()
    {
        var other = Stay(petId: Guid.NewGuid());
        (await CreateHandler().Handle(Cmd(other.Id), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.HospitalizationPetMismatch");

        var otherClinic = Stay(clinicId: Guid.NewGuid());
        (await CreateHandler().Handle(Cmd(otherClinic.Id), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.HospitalizationClinicMismatch");

        var stay = Stay(admittedAt: Now.AddHours(-3));
        (await CreateHandler().Handle(Cmd(stay.Id, at: Now.AddHours(-4)), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.OutsideHospitalization");
        (await CreateHandler().Handle(Cmd(stay.Id, at: Now.AddHours(-2)), CancellationToken.None)).IsSuccess.Should().BeTrue();

        stay.Cancel(Guid.NewGuid(), "Yanlış hastaya açıldı", Now).IsSuccess.Should().BeTrue();
        (await CreateHandler().Handle(Cmd(stay.Id, at: Now.AddHours(-2)), CancellationToken.None)).Error.Code.Should().Be("Hospitalizations.AlreadyCancelled");
    }

    [Fact]
    public async Task Create_On_Finalized_Examination_Should_Require_Late_Entry_Reason()
    {
        var exam = Exam(finalized: true);

        (await CreateHandler().Handle(Cmd(examinationId: exam.Id), CancellationToken.None)).Error.Code
            .Should().Be(VitalMeasurement.LateEntryReasonRequiredCode);

        VitalMeasurement? added = null;
        _write.Setup(r => r.AddAsync(It.IsAny<VitalMeasurement>(), It.IsAny<CancellationToken>()))
            .Callback<VitalMeasurement, CancellationToken>((m, _) => added = m)
            .ReturnsAsync((VitalMeasurement m, CancellationToken _) => m);
        (await CreateHandler().Handle(Cmd(examinationId: exam.Id, lateReason: "Kesinleştirmeden sonra ölçüldü"), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        added!.AddedAfterFinalization.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Reject_Examination_Of_Other_Pet_And_Unassigned_Clinic()
    {
        var exam = Exam(petId: Guid.NewGuid());
        (await CreateHandler().Handle(Cmd(examinationId: exam.Id), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.ExaminationPetMismatch");

        _scopeResolver.SetupAccessDenied();
        (await CreateHandler().Handle(Cmd(), CancellationToken.None)).Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Update_Should_Require_Version_And_Map_Concurrency_Conflict()
    {
        var m = Existing();
        var update = new UpdateVitalMeasurementCommand(m.Id, Now.AddMinutes(-10), 39.1m, null, null, null, "Titreme", null);

        (await UpdateHandler().Handle(update, CancellationToken.None)).Error.Code.Should().Be(VitalMeasurement.ValidationCode);

        _write.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());
        (await UpdateHandler().Handle(update with { RowVersion = Version }, CancellationToken.None)).Error.Code
            .Should().Be(VitalMeasurementConcurrency.ConflictCode);
        m.TemperatureC.Should().Be(39.1m);
    }

    [Fact]
    public async Task Update_Should_Be_Locked_By_Finalized_Examination_And_Hospitalization_Window()
    {
        var exam = Exam(finalized: true);
        var linked = Existing(examinationId: exam.Id);
        (await UpdateHandler().Handle(new UpdateVitalMeasurementCommand(linked.Id, Now, 38m, null, null, null, null, Version), CancellationToken.None))
            .Error.Code.Should().Be("Examinations.Finalized");

        var stay = Stay(admittedAt: Now.AddHours(-3));
        var inStay = Existing(hospitalizationId: stay.Id);
        (await UpdateHandler().Handle(new UpdateVitalMeasurementCommand(inStay.Id, Now.AddHours(-5), 38m, null, null, null, null, Version), CancellationToken.None))
            .Error.Code.Should().Be("VitalMeasurements.OutsideHospitalization");
        (await UpdateHandler().Handle(new UpdateVitalMeasurementCommand(inStay.Id, Now.AddHours(-1), 38m, null, null, null, null, Version), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Write_And_Read_Should_Return_NotFound_Outside_Clinic_Scope()
    {
        var m = Existing(clinicId: Guid.NewGuid());
        _scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin([_clinicId]);

        (await UpdateHandler().Handle(new UpdateVitalMeasurementCommand(m.Id, Now, 38m, null, null, null, null, Version), CancellationToken.None))
            .Error.Code.Should().Be("VitalMeasurements.NotFound");
        (await CancelHandler().Handle(new CancelVitalMeasurementCommand(m.Id, "Yanlış hastaya girildi", Version), CancellationToken.None))
            .Error.Code.Should().Be("VitalMeasurements.NotFound");

        var get = new GetVitalMeasurementByIdQueryHandler(_tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _read.Object, _pets.Object);
        (await get.Handle(new GetVitalMeasurementByIdQuery(m.Id), CancellationToken.None)).Error.Code.Should().Be("VitalMeasurements.NotFound");
    }

    [Fact]
    public async Task Cancel_Should_Require_Reason_Then_Be_Final()
    {
        var m = Existing();

        (await CancelHandler().Handle(new CancelVitalMeasurementCommand(m.Id, "x", Version), CancellationToken.None))
            .Error.Code.Should().Be(VitalMeasurement.ValidationCode);
        (await CancelHandler().Handle(new CancelVitalMeasurementCommand(m.Id, "Yanlış hastaya girildi", Version), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        m.CancelledByUserId.Should().Be(_userId);
        (await CancelHandler().Handle(new CancelVitalMeasurementCommand(m.Id, "Yanlış hastaya girildi", Version), CancellationToken.None))
            .Error.Code.Should().Be(VitalMeasurement.AlreadyCancelledCode);
    }

    [Fact]
    public async Task Series_Should_Merge_Sources_Ascending_And_Keep_Newest_Within_Limit()
    {
        var examId = Guid.NewGuid();
        _read.Setup(r => r.ListAsync(It.IsAny<VitalMeasurementSeriesSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Point(VitalSeriesSources.VitalMeasurement, Now.AddHours(-1), weight: null),
            Point(VitalSeriesSources.VitalMeasurement, Now.AddHours(-5), weight: null),
        ]);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationVitalSeriesSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Point(VitalSeriesSources.Examination, Now.AddHours(-3), weight: 12.6m, id: examId),
        ]);

        var all = await SeriesHandler().Handle(new GetVitalSeriesQuery(_petId), CancellationToken.None);
        all.Value!.Points.Select(p => p.MeasuredAtUtc).Should().BeInAscendingOrder();
        all.Value.Points.Should().HaveCount(3);
        all.Value.Points[1].Source.Should().Be(VitalSeriesSources.Examination);
        all.Value.Points[1].WeightKg.Should().Be(12.6m);
        all.Value.Truncated.Should().BeFalse();
        all.Value.OmittedSources.Should().BeEmpty();

        var limited = await SeriesHandler().Handle(new GetVitalSeriesQuery(_petId, Limit: 2), CancellationToken.None);
        limited.Value!.Truncated.Should().BeTrue();
        limited.Value.Points.Select(p => p.MeasuredAtUtc).Should().Equal(Now.AddHours(-3), Now.AddHours(-1));
    }

    [Fact]
    public async Task Series_Should_Omit_Examinations_Without_Permission()
    {
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Examinations.Read)).Returns(false);
        _read.Setup(r => r.ListAsync(It.IsAny<VitalMeasurementSeriesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Point(VitalSeriesSources.VitalMeasurement, Now.AddHours(-1), weight: 12m)]);

        var result = await SeriesHandler().Handle(new GetVitalSeriesQuery(_petId), CancellationToken.None);

        result.Value!.OmittedSources.Should().Equal(VitalSeriesSources.Examination);
        result.Value.Points.Should().ContainSingle();
        _examinations.Verify(r => r.ListAsync(It.IsAny<ExaminationVitalSeriesSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Series_Should_Return_NotFound_For_Other_Pets_Hospitalization()
    {
        var stay = Stay(petId: Guid.NewGuid());

        var result = await SeriesHandler().Handle(new GetVitalSeriesQuery(_petId, HospitalizationId: stay.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    private VitalSeriesPointDto Point(string source, DateTime at, decimal? weight, Guid? id = null)
        => new(source, id ?? Guid.NewGuid(), _clinicId, at, 38.5m, null, null, weight, null, null);
}
