using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Access;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Hospitalizations.Wards;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Access;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Update;

public sealed class UpdateHospitalizationCommandHandler
    : IRequestHandler<UpdateHospitalizationCommand, Result<HospitalizationWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Visit> _visits;
    private readonly IReadRepository<Hospitalization> _hospitalizationsRead;
    private readonly IRepository<Hospitalization> _hospitalizationsWrite;
    private readonly IReadRepository<HospitalizationRoom> _rooms;
    private readonly IReadRepository<HospitalizationWard> _wards;
    private readonly TimeProvider _timeProvider;

    public UpdateHospitalizationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<Visit> visits,
        IReadRepository<Hospitalization> hospitalizationsRead,
        IRepository<Hospitalization> hospitalizationsWrite,
        IReadRepository<HospitalizationRoom> rooms,
        IReadRepository<HospitalizationWard> wards,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _visits = visits;
        _hospitalizationsRead = hospitalizationsRead;
        _hospitalizationsWrite = hospitalizationsWrite;
        _rooms = rooms;
        _wards = wards;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<HospitalizationWriteResultDto>> Handle(UpdateHospitalizationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Fail(
                "Tenants.TenantInactive",
                "Pasif kiracı için yatış kaydı güncellenemez.");
        }

        var existing = await _hospitalizationsWrite.FirstOrDefaultAsync(
            new HospitalizationForUpdateByIdSpec(tenantId, request.Id), ct);
        if (existing is null)
            return Fail("Hospitalizations.NotFound", "Yatış kaydı bulunamadı.");

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Fail(
                "Hospitalizations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            effectiveClinicId = existing.ClinicId;

        if (effectiveClinicId == Guid.Empty)
            return Fail("Hospitalizations.Validation", "ClinicId is required.");

        var clinicAccess = await HospitalizationClinicWriteScope.EnsureEntityAndTargetWriteAccessAsync(
            _clinicScopeResolver, tenantId, existing.ClinicId, effectiveClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(clinicAccess.Error);

        if (_clinicContext.ClinicId is { } currentClinicId && effectiveClinicId != currentClinicId)
        {
            return Fail(
                "Hospitalizations.ClinicContextMismatch",
                "Yatış kaydı sadece aktif clinic bağlamında güncellenebilir.");
        }

        if (!ExaminationRowVersion.TryDecodeOptional(request.RowVersion, out var expectedRowVersion))
            return Fail("Hospitalizations.Validation", "RowVersion geçerli Base64 (8 bayt) olmalıdır.");

        var admittedUtc = AdmittedAtUtcWindow.ToUtc(request.AdmittedAtUtc);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var window = AdmittedAtUtcWindow.Validate(admittedUtc, utcNow);
        if (!window.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(window.Error);

        if (request.PlannedDischargeAtUtc.HasValue
            && AdmittedAtUtcWindow.ToUtc(request.PlannedDischargeAtUtc.Value) < admittedUtc)
        {
            return Fail(
                "Hospitalizations.PlannedDischargeBeforeAdmission",
                "Planned discharge must not be before admission.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Fail("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Fail("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        Examination? exam = null;
        if (request.ExaminationId is { } examId)
        {
            exam = await _examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, examId), ct);
            if (exam is null)
            {
                return Fail(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId)
            {
                return Fail(
                    "Hospitalizations.ExaminationClinicMismatch",
                    "Examination clinic does not match hospitalization clinic.");
            }

            if (exam.PetId != request.PetId)
            {
                return Fail(
                    "Hospitalizations.ExaminationPetMismatch",
                    "Examination pet does not match hospitalization pet.");
            }
        }

        // Taburcu edilmiş kayıt hiçbir alanı değiştirmez; kilit hatası yerine durum hatası dönmeli.
        if (existing.DischargedAtUtc.HasValue)
            return Fail("Hospitalizations.AlreadyDischarged", "Discharged hospitalization cannot be updated.");

        var locked = await ExaminationLinkedRecordLock.EnsureHospitalizationLinkEditableAsync(
            _examinations, tenantId, existing.ExaminationId, request.ExaminationId, ct);
        if (!locked.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(locked.Error);

        var otherActive = await _hospitalizationsRead.AnyAsync(
            new ActiveHospitalizationForPetClinicSpec(tenantId, effectiveClinicId, request.PetId, request.Id),
            ct);
        if (otherActive)
        {
            return Fail(
                "Hospitalizations.ActiveHospitalizationExists",
                "An active hospitalization already exists for this pet at this clinic.");
        }

        var visitHint = request.VisitId
            ?? (request.ExaminationId == existing.ExaminationId ? existing.VisitId : null);
        var visit = await LinkedRecordVisitResolver.ResolveAsync(
            _visits, tenantId, effectiveClinicId, request.PetId, exam, visitHint, "Hospitalizations", "yatış kaydı", ct);
        if (!visit.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(visit.Error);

        // Oda, UpdateDetails'ten önce çözülür ki hata durumunda kayıt hiç değişmesin; aynı oda yeniden gönderilirse
        // pasifleşmiş olsa da korunur (mevcut yatış etkilenmez).
        HospitalizationRoomAssignment? roomAssignment = null;
        if (request.RoomId is { } roomId)
        {
            var room = await HospitalizationRoomAssignmentResolver.ResolveAsync(
                _rooms, _wards, tenantId, effectiveClinicId, roomId, existing.RoomId, ct);
            if (!room.IsSuccess)
                return Result<HospitalizationWriteResultDto>.Failure(room.Error);
            roomAssignment = room.Value;
        }

        var update = existing.UpdateDetails(
            effectiveClinicId,
            request.PetId,
            request.ExaminationId,
            visit.Value,
            admittedUtc,
            request.PlannedDischargeAtUtc,
            request.Reason,
            request.Notes,
            request.Location,
            utcNow);

        if (!update.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(update.Error);

        // Klinik değişince eski kliniğin odası bağlı kalamaz; açık oda verilmediyse bağ kaldırılır (konum metni kalır).
        var clearRoom = request.ClearRoom || (roomAssignment is null && existing.RoomId.HasValue && existing.ClinicId != effectiveClinicId);
        var roomChange = roomAssignment is not null
            ? existing.AssignRoom(roomAssignment.Room, roomAssignment.Ward)
            : clearRoom ? existing.ClearRoom() : Result.Success();
        if (!roomChange.IsSuccess)
            return Result<HospitalizationWriteResultDto>.Failure(roomChange.Error);

        if (expectedRowVersion is not null)
            existing.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _hospitalizationsWrite.UpdateAsync(existing, ct);
            await _hospitalizationsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "Hospitalizations.ConcurrencyConflict",
                "Yatış kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.");
        }

        return Result<HospitalizationWriteResultDto>.Success(
            new HospitalizationWriteResultDto(existing.Id, ExaminationRowVersion.Encode(existing.RowVersion)));
    }

    private static Result<HospitalizationWriteResultDto> Fail(string code, string message)
        => Result<HospitalizationWriteResultDto>.Failure(code, message);
}
