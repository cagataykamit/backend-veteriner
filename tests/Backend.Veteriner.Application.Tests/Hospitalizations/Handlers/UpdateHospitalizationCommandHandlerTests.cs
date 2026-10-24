using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Commands.Update;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Handlers;

public sealed class UpdateHospitalizationCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Visit>> _visits = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizationsRead = new();
    private readonly Mock<IRepository<Hospitalization>> _hospitalizationsWrite = new();
    private readonly Mock<IReadRepository<HospitalizationRoom>> _rooms = new();
    private readonly Mock<IReadRepository<HospitalizationWard>> _wards = new();

    private UpdateHospitalizationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _examinations.Object,
            _visits.Object,
            _hospitalizationsRead.Object,
            _hospitalizationsWrite.Object,
            _rooms.Object,
            _wards.Object);

    private static UpdateHospitalizationCommand Cmd(
        Guid id,
        Guid clinicId,
        Guid petId,
        Guid? examinationId = null,
        DateTime? admittedAt = null,
        DateTime? planned = null)
        => new(
            id,
            clinicId,
            petId,
            examinationId,
            admittedAt ?? DateTime.UtcNow.AddHours(-1),
            planned,
            "Güncel neden",
            null);

    private void SetupTenantClinicPet(Guid tid, Guid cid, Guid petId)
    {
        var pet = new Pet(tid, Guid.NewGuid(), "P", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);

        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
    }

    private static Hospitalization OpenStay(Guid tid, Guid cid, Guid petId)
        => new(
            tid,
            cid,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            null,
            "Eski",
            null);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantInactive()
    {
        var tid = Guid.NewGuid();
        var tenant = new Tenant("A");
        tenant.Deactivate();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await CreateHandler().Handle(
            Cmd(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _hospitalizationsWrite.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AlreadyDischarged()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = OpenStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(existing, hid);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!
            .SetValue(existing, DateTime.UtcNow);

        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(Cmd(hid, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AlreadyDischarged");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AdmittedAtTooFarInPast()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = OpenStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(existing, hid);

        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(hid, cid, petId, admittedAt: DateTime.UtcNow.AddDays(-10)),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.AdmittedAtTooFarInPast");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ActiveHospitalizationExists()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));

        var existing = OpenStay(tid, cid, petId);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(existing, hid);

        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(Cmd(hid, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ActiveHospitalizationExists");
    }

    [Fact]
    public async Task Handle_Should_Update_When_Valid()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exam = new Examination(tid, cid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exam);

        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId, examinationId: examId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.ExaminationId.Should().Be(examId);
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Examination ExamFor(Guid tid, Guid cid, Guid petId, Guid examId, Guid? visitId = null, bool finalized = false)
    {
        var exam = new Examination(tid, cid, petId, null, DateTime.UtcNow, "V", "F", null, null);
        typeof(Examination).GetProperty(nameof(Examination.Id))!.SetValue(exam, examId);
        typeof(Examination).GetProperty(nameof(Examination.VisitId))!.SetValue(exam, visitId);
        if (finalized)
            exam.Finalize(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return exam;
    }

    private static Visit VisitFor(Guid tid, Guid cid, Guid petId, Guid visitId)
    {
        var visit = new Visit(tid, cid, petId, null, null, Guid.NewGuid(), DateTime.UtcNow);
        typeof(Visit).GetProperty(nameof(Visit.Id))!.SetValue(visit, visitId);
        return visit;
    }

    private void ArrangeUpdate(Hospitalization existing, Guid tid, Guid cid, Guid petId, Examination? exam = null, Visit? visit = null)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(cid);
        _hospitalizationsWrite.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        SetupTenantClinicPet(tid, cid, petId);
        _hospitalizationsRead.Setup(r => r.AnyAsync(It.IsAny<ActiveHospitalizationForPetClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _hospitalizationsWrite.Setup(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        if (exam is not null)
        {
            _examinations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ExaminationByIdSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(exam);
        }

        if (visit is not null)
        {
            _visits.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(visit);
        }
    }

    [Fact]
    public async Task Handle_Should_Reject_When_Linking_To_Finalized_Examination()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId, ExamFor(tid, cid, petId, examId, finalized: true));

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId, examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Finalized");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Reject_When_Detaching_From_Finalized_Examination()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var existing = new Hospitalization(tid, cid, petId, examId, DateTime.UtcNow.AddDays(-1), null, "Eski", null);
        ArrangeUpdate(existing, tid, cid, petId, ExamFor(tid, cid, petId, examId, finalized: true));

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId, examinationId: null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Examinations.Finalized");
    }

    [Fact]
    public async Task Handle_Should_Allow_Other_Changes_When_Examination_Link_Unchanged_And_Finalized()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var existing = new Hospitalization(tid, cid, petId, examId, DateTime.UtcNow.AddDays(-1), null, "Eski", null);
        ArrangeUpdate(existing, tid, cid, petId, ExamFor(tid, cid, petId, examId, finalized: true));

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId, examinationId: examId) with { Location = "Kafes 3" },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Location.Should().Be("Kafes 3");
        existing.Reason.Should().Be("Güncel neden");
    }

    [Fact]
    public async Task Handle_Should_Keep_Location_And_Visit_When_Omitted()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var existing = new Hospitalization(
            tid, cid, petId, null, DateTime.UtcNow.AddDays(-1), null, "Eski", null, visitId, "Kafes 1");
        ArrangeUpdate(existing, tid, cid, petId, visit: VisitFor(tid, cid, petId, visitId));

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Location.Should().Be("Kafes 1");
        existing.VisitId.Should().Be(visitId);
    }

    [Fact]
    public async Task Handle_Should_Inherit_Visit_From_New_Examination()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(
            existing, tid, cid, petId,
            ExamFor(tid, cid, petId, examId, visitId),
            VisitFor(tid, cid, petId, visitId));

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId, examinationId: examId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.VisitId.Should().Be(visitId);
    }

    [Fact]
    public async Task Handle_Should_Reject_When_Visit_Conflicts_With_Examination_Visit()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId, ExamFor(tid, cid, petId, examId, Guid.NewGuid()));

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId, examinationId: examId) with { VisitId = Guid.NewGuid() },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ExaminationVisitMismatch");
    }

    [Fact]
    public async Task Handle_Should_Reject_When_Visit_Belongs_To_Another_Pet()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId, visit: VisitFor(tid, cid, Guid.NewGuid(), visitId));

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId) with { VisitId = visitId },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.VisitPetMismatch");
    }

    [Fact]
    public async Task Handle_Should_Reject_Malformed_RowVersion()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId);

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId) with { RowVersion = "not-base64" },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.Validation");
    }

    [Fact]
    public async Task Handle_Should_Use_Client_RowVersion_And_Return_New_One()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId);
        var sent = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var result = await CreateHandler().Handle(
            Cmd(existing.Id, cid, petId) with { RowVersion = Convert.ToBase64String(sent) },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.RowVersion.Should().Equal(sent);
        result.Value!.Id.Should().Be(existing.Id);
        result.Value!.RowVersion.Should().Be(Convert.ToBase64String(sent));
    }

    [Fact]
    public async Task Handle_Should_Return_ConcurrencyConflict_When_Save_Detects_Newer_Version()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId);
        _hospitalizationsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.ConcurrencyConflict");
    }

    // ---------------- HOSP-002: oda ataması ----------------

    private (HospitalizationWard Ward, HospitalizationRoom Room) ArrangeRoom(Guid tid, Guid cid, bool roomActive = true)
    {
        var ward = HospitalizationWard.Create(tid, cid, "Yoğun bakım", 1, DateTime.UtcNow);
        var room = HospitalizationRoom.Create(ward, "Kafes 2", 1, DateTime.UtcNow);
        if (!roomActive) room.SetActive(false, DateTime.UtcNow);
        _rooms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationRoomByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(room);
        _wards.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationWardByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ward);
        return (ward, room);
    }

    [Fact]
    public async Task Handle_Should_Return_RoomInactive_And_Not_Mutate_When_New_Room_Is_Passive()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        ArrangeUpdate(existing, tid, cid, petId);
        var (_, room) = ArrangeRoom(tid, cid, roomActive: false);

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId) with { RoomId = room.Id }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.RoomInactive");
        existing.Reason.Should().Be("Eski");
        _hospitalizationsWrite.Verify(r => r.UpdateAsync(It.IsAny<Hospitalization>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Keep_Same_Room_When_It_Became_Passive()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        var (ward, room) = ArrangeRoom(tid, cid, roomActive: false);
        room.SetActive(true, DateTime.UtcNow);
        existing.AssignRoom(room, ward).IsSuccess.Should().BeTrue();
        room.SetActive(false, DateTime.UtcNow);
        ArrangeUpdate(existing, tid, cid, petId);

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId) with { RoomId = room.Id }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.RoomId.Should().Be(room.Id);
        existing.Location.Should().Be("Yoğun bakım / Kafes 2");
    }

    [Fact]
    public async Task Handle_Should_Leave_Room_Untouched_When_RoomId_Omitted()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        var (ward, room) = ArrangeRoom(tid, cid);
        existing.AssignRoom(room, ward).IsSuccess.Should().BeTrue();
        ArrangeUpdate(existing, tid, cid, petId);

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.RoomId.Should().Be(room.Id);
        _rooms.Verify(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationRoomByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Clear_Room_And_Keep_Location_Text_When_ClearRoom()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var existing = OpenStay(tid, cid, petId);
        var (ward, room) = ArrangeRoom(tid, cid);
        existing.AssignRoom(room, ward).IsSuccess.Should().BeTrue();
        ArrangeUpdate(existing, tid, cid, petId);

        var result = await CreateHandler().Handle(Cmd(existing.Id, cid, petId) with { ClearRoom = true }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.RoomId.Should().BeNull();
        existing.Location.Should().Be("Yoğun bakım / Kafes 2");
    }
}
