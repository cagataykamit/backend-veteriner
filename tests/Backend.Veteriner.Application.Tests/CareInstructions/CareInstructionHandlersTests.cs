using Backend.Veteriner.Application.CareInstructions.Commands.Create;
using Backend.Veteriner.Application.CareInstructions.Commands.Update;
using Backend.Veteriner.Application.CareInstructions.Queries.GetById;
using Backend.Veteriner.Application.CareInstructions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.CareInstructions;

public sealed class CareInstructionHandlersTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Visit>> _visits = new();
    private readonly Mock<IRepository<CareInstruction>> _write = new();
    private readonly Mock<IReadRepository<CareInstruction>> _read = new();

    public CareInstructionHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_clinicId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(_tenantId, "K", "İstanbul"));
        var pet = new Pet(_tenantId, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, _petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _write.Setup(r => r.AddAsync(It.IsAny<CareInstruction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CareInstruction c, CancellationToken _) => c);
        _write.Setup(r => r.UpdateAsync(It.IsAny<CareInstruction>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _write.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CreateCareInstructionCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _hospitalizations.Object,
            _examinations.Object,
            _visits.Object,
            _write.Object);

    private CreateCareInstructionCommand Cmd(Guid? hospitalizationId = null, Guid? examinationId = null, Guid? visitId = null)
        => new(_clinicId, _petId, hospitalizationId, examinationId, visitId, "Günde iki kez su verin");

    private Hospitalization Stay(Guid? examinationId = null, Guid? visitId = null, Guid? petId = null, Guid? clinicId = null)
    {
        var h = new Hospitalization(
            _tenantId, clinicId ?? _clinicId, petId ?? _petId, examinationId, DateTime.UtcNow.AddHours(-2), null, "Gözlem", null, visitId);
        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(h);
        return h;
    }

    private Examination Exam(Guid id, Guid? visitId = null, bool finalized = false)
    {
        var e = new Examination(_tenantId, _clinicId, _petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(e, id);
        typeof(Examination).GetProperty(nameof(Examination.VisitId))!.SetValue(e, visitId);
        if (finalized)
            e.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(e);
        return e;
    }

    private void VisitExists(Guid id)
    {
        var v = new Visit(_tenantId, _clinicId, _petId, null, null, Guid.NewGuid(), DateTime.UtcNow);
        typeof(Visit).GetProperty(nameof(Visit.Id))!.SetValue(v, id);
        _visits.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(v);
    }

    [Fact]
    public async Task Create_Should_Persist_Standalone_Instruction_With_Author()
    {
        CareInstruction? added = null;
        _write.Setup(r => r.AddAsync(It.IsAny<CareInstruction>(), It.IsAny<CancellationToken>()))
            .Callback<CareInstruction, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((CareInstruction c, CancellationToken _) => c);

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.CreatedByUserId.Should().Be(_userId);
        added.HospitalizationId.Should().BeNull();
        added.Text.Should().Be("Günde iki kez su verin");
    }

    [Fact]
    public async Task Create_Should_Inherit_Examination_And_Visit_From_Hospitalization()
    {
        var examId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var stay = Stay(examId, visitId);
        Exam(examId, visitId);
        VisitExists(visitId);
        CareInstruction? added = null;
        _write.Setup(r => r.AddAsync(It.IsAny<CareInstruction>(), It.IsAny<CancellationToken>()))
            .Callback<CareInstruction, CancellationToken>((c, _) => added = c)
            .ReturnsAsync((CareInstruction c, CancellationToken _) => c);

        var result = await CreateHandler().Handle(Cmd(hospitalizationId: stay.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.HospitalizationId.Should().Be(stay.Id);
        added.ExaminationId.Should().Be(examId);
        added.VisitId.Should().Be(visitId);
    }

    [Fact]
    public async Task Create_Should_Be_Allowed_For_Finalized_Examination()
    {
        var examId = Guid.NewGuid();
        Exam(examId, finalized: true);

        var result = await CreateHandler().Handle(Cmd(examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Be_Allowed_For_Discharged_Hospitalization()
    {
        var stay = Stay();
        stay.Discharge(DateTime.UtcNow, null).IsSuccess.Should().BeTrue();

        var result = await CreateHandler().Handle(Cmd(hospitalizationId: stay.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Should_Reject_Hospitalization_Of_Another_Pet()
    {
        var stay = Stay(petId: Guid.NewGuid());

        var result = await CreateHandler().Handle(Cmd(hospitalizationId: stay.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.HospitalizationPetMismatch");
    }

    [Fact]
    public async Task Create_Should_Reject_Hospitalization_Of_Another_Clinic()
    {
        var stay = Stay(clinicId: Guid.NewGuid());

        var result = await CreateHandler().Handle(Cmd(hospitalizationId: stay.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.HospitalizationClinicMismatch");
    }

    [Fact]
    public async Task Create_Should_Return_NotFound_When_Hospitalization_Missing()
    {
        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateHandler().Handle(Cmd(hospitalizationId: Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Create_Should_Reject_Examination_That_Differs_From_Hospitalization_Examination()
    {
        var stay = Stay(examinationId: Guid.NewGuid());

        var result = await CreateHandler().Handle(
            Cmd(hospitalizationId: stay.Id, examinationId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.HospitalizationExaminationMismatch");
    }

    [Fact]
    public async Task Create_Should_Reject_Visit_That_Differs_From_Hospitalization_Visit()
    {
        var stay = Stay(visitId: Guid.NewGuid());

        var result = await CreateHandler().Handle(
            Cmd(hospitalizationId: stay.Id, visitId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.VisitMismatch");
    }

    [Fact]
    public async Task Create_Should_Reject_Visit_That_Differs_From_Examination_Visit()
    {
        var examId = Guid.NewGuid();
        Exam(examId, Guid.NewGuid());

        var result = await CreateHandler().Handle(
            Cmd(examinationId: examId, visitId: Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.ExaminationVisitMismatch");
    }

    [Fact]
    public async Task Create_Should_Reject_When_Clinic_Context_Differs()
    {
        var result = await CreateHandler().Handle(
            Cmd() with { ClinicId = Guid.NewGuid() },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.ClinicContextMismatch");
    }

    [Fact]
    public async Task Create_Should_Reject_When_User_Context_Missing()
    {
        _clientContext.SetupGet(c => c.UserId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    private UpdateCareInstructionCommandHandler UpdateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _write.Object);

    private CareInstruction Existing()
    {
        var c = new CareInstruction(_tenantId, _clinicId, _petId, null, null, null, Guid.NewGuid(), "Eski");
        _write.Setup(r => r.FirstOrDefaultAsync(It.IsAny<CareInstructionForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(c);
        return c;
    }

    private static string Version(byte seed) => Convert.ToBase64String(Enumerable.Repeat(seed, 8).ToArray());

    [Fact]
    public async Task Update_Should_Change_Text_Apply_RowVersion_And_Return_It()
    {
        var c = Existing();

        var result = await UpdateHandler().Handle(
            new UpdateCareInstructionCommand(c.Id, "Yeni", Version(3)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        c.Text.Should().Be("Yeni");
        c.UpdatedByUserId.Should().Be(_userId);
        result.Value!.RowVersion.Should().Be(Version(3));
    }

    [Fact]
    public async Task Update_Should_Require_Valid_RowVersion()
    {
        var c = Existing();

        var result = await UpdateHandler().Handle(
            new UpdateCareInstructionCommand(c.Id, "Yeni", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.Validation");
        c.Text.Should().Be("Eski");
    }

    [Fact]
    public async Task Update_Should_Return_ConcurrencyConflict_When_Save_Detects_Newer_Version()
    {
        var c = Existing();
        _write.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await UpdateHandler().Handle(
            new UpdateCareInstructionCommand(c.Id, "Yeni", Version(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.ConcurrencyConflict");
    }

    [Fact]
    public async Task Update_Should_Return_NotFound_When_Instruction_Is_In_Another_Clinic()
    {
        var other = new CareInstruction(_tenantId, Guid.NewGuid(), _petId, null, null, null, Guid.NewGuid(), "Eski");
        _write.Setup(r => r.FirstOrDefaultAsync(It.IsAny<CareInstructionForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var result = await UpdateHandler().Handle(
            new UpdateCareInstructionCommand(other.Id, "Yeni", Version(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Hide_Instruction_Of_Unassigned_Clinic()
    {
        var c = new CareInstruction(_tenantId, _clinicId, _petId, null, null, null, Guid.NewGuid(), "Metin");
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<CareInstructionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(c);
        var denied = ClinicReadScopeResolverMock.ForClinicAdmin([Guid.NewGuid()]);
        var handler = new GetCareInstructionByIdQueryHandler(
            _tenantContext.Object, new Mock<IClinicContext>().Object, denied.Object, _read.Object);

        var result = await handler.Handle(new GetCareInstructionByIdQuery(c.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.NotFound");
    }

    [Fact]
    public async Task GetById_Should_Return_Dto_With_RowVersion()
    {
        var c = new CareInstruction(_tenantId, _clinicId, _petId, null, null, null, Guid.NewGuid(), "Metin");
        c.SetExpectedRowVersion(Enumerable.Repeat((byte)9, 8).ToArray());
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<CareInstructionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(c);
        var handler = new GetCareInstructionByIdQueryHandler(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, _read.Object);

        var result = await handler.Handle(new GetCareInstructionByIdQuery(c.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Text.Should().Be("Metin");
        result.Value!.RowVersion.Should().Be(Version(9));
    }
}
