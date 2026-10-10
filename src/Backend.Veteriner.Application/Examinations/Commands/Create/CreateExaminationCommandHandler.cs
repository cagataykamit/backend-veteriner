using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Examinations.Commands.Create;

public sealed class CreateExaminationCommandHandler : IRequestHandler<CreateExaminationCommand, Result<ExaminationWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IRepository<Appointment> _appointmentsWrite;
    private readonly IReadRepository<Visit> _visits;
    private readonly TimeProvider _timeProvider;
    private readonly IRepository<Examination> _examinationsWrite;

    public CreateExaminationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IRepository<Appointment> appointmentsWrite,
        IRepository<Examination> examinationsWrite,
        IReadRepository<Visit> visits,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _appointments = appointments;
        _appointmentsWrite = appointmentsWrite;
        _examinationsWrite = examinationsWrite;
        _visits = visits;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ExaminationWriteResultDto>> Handle(CreateExaminationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<ExaminationWriteResultDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için muayene kaydı oluşturulamaz.");
        }

        var examinedUtc = ExaminationExaminedAtWindow.ToUtc(request.ExaminedAtUtc);
        var window = ExaminationExaminedAtWindow.Validate(examinedUtc);

        if (!window.IsSuccess)

            return Result<ExaminationWriteResultDto>.Failure(window.Error);
        // Geliş (Visit) verilmişse hayvan/klinik/randevu ondan türetilir; hasta tekrar seçilmez.
        var requestClinicId = request.ClinicId;
        var requestPetId = request.PetId;
        var requestAppointmentId = request.AppointmentId;

        Visit? visit = null;
        if (request.VisitId is { } visitId)
        {
            visit = await _visits.FirstOrDefaultAsync(new VisitByIdSpec(tenantId, visitId), ct);
            if (visit is null
                || (_clinicContext.ClinicId is { } contextClinicId && visit.ClinicId != contextClinicId))
            {
                return Result<ExaminationWriteResultDto>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");
            }

            if (visit.IsVoided || visit.CareStatus == VisitCareStatus.Completed)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Visits.NotOpen",
                    "Tamamlanmış veya yanlış geliş işaretli kayıt için muayene açılamaz.");
            }

            requestClinicId ??= visit.ClinicId;
            requestPetId ??= visit.PetId;
            requestAppointmentId ??= visit.AppointmentId;
        }


        Guid clinicId;
        Guid petId;

        Appointment? appt = null;
        if (requestAppointmentId is { } aid)
        {
            appt = await _appointments.FirstOrDefaultAsync(
                new AppointmentByIdSpec(tenantId, aid), ct);
            if (appt is null)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Appointments.NotFound",
                    "Randevu bulunamadı veya kiracıya ait değil.");
            }

            // İptal edilmiş randevuya muayene eklemek ürün açısından yanlıştır.
            // Erken fail; daha ileri validasyon/klinik-pet doğrulaması yapılmaz,
            // SaveChanges çağrılmaz.
            if (appt.Status == AppointmentStatus.Cancelled)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.AppointmentCancelled",
                    "İptal edilmiş randevu için muayene kaydı oluşturulamaz.");
            }

            // Gelmedi işaretli randevuya doğrudan muayene açılmaz; hasta geldiyse önce geliş açılır (randevu Scheduled'a döner).
            if (appt.Status == AppointmentStatus.NoShow)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.AppointmentNoShow",
                    "Gelmedi işaretli randevu için muayene kaydı oluşturulamaz; önce geliş kaydı açın veya işareti geri alın.");
            }

            if (requestClinicId.HasValue && _clinicContext.ClinicId.HasValue && requestClinicId.Value != _clinicContext.ClinicId.Value)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.ClinicContextMismatch",
                    "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
            }

            clinicId = _clinicContext.ClinicId ?? requestClinicId ?? appt.ClinicId;
            petId = requestPetId ?? appt.PetId;

            if (clinicId != appt.ClinicId || petId != appt.PetId)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.AppointmentPetClinicMismatch",
                    "Seçilen randevu ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }
        else
        {
            var cid = _clinicContext.ClinicId ?? requestClinicId;
            if (cid is not { } resolvedCid || resolvedCid == Guid.Empty
                || requestPetId is not { } pid || pid == Guid.Empty)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.Validation",
                    "AppointmentId yoksa ClinicId ve PetId zorunludur.");
            }

            clinicId = resolvedCid;
            petId = pid;
        }

        if (visit is not null
            && (clinicId != visit.ClinicId || petId != visit.PetId || requestAppointmentId != visit.AppointmentId))
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Examinations.VisitMismatch",
                "Muayene isteği geliş kaydının klinik, hayvan veya randevusuyla uyuşmuyor.");
        }

        var clinicAccess = await ExaminationClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, clinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ExaminationWriteResultDto>.Failure(clinicAccess.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result<ExaminationWriteResultDto>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, petId), ct);
        if (pet is null)
            return Result<ExaminationWriteResultDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var examination = new Examination(
            tenantId,
            clinicId,
            petId,
            requestAppointmentId,
            examinedUtc,
            request.VisitReason,
            request.Findings,
            request.Assessment,
            request.Notes,
            request.Anamnesis,
            request.Plan,
            request.WeightKg,
            request.TemperatureC,
            request.HeartRateBpm,
            request.RespiratoryRatePerMin,
            request.VitalsMeasuredAtUtc,
            visit?.Id);

        // Muayene başlayınca bekleyen geliş "Devam ediyor" olur. Repository AddAsync kaydı hemen kalıcılaştırdığı
        // (SaveChanges) için geliş geçişi AddAsync öncesinde yapılır; muayene ve geliş tek SaveChanges'te atomiktir.
        if (visit is { CareStatus: VisitCareStatus.Waiting })
        {
            var started = visit.Start(_timeProvider.GetUtcNow().UtcDateTime);
            if (!started.IsSuccess)
                return Result<ExaminationWriteResultDto>.Failure(started.Error);
        }

        try
        {
            await _examinationsWrite.AddAsync(examination, ct);
        }
        catch (DbUpdateConcurrencyException) when (visit is not null)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Visits.ConcurrencyConflict",
                "Geliş kaydı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        // Appointment lifecycle: muayene başarıyla eklendiyse ve akış bir randevuya
        // bağlıysa, planlanmış randevuyu otomatik olarak tamamla. Completed/Cancelled
        // durumları için mevcut kural korunur (no-op). Ödeme akışı bu mantığa hiçbir
        // zaman dokunmaz (CreatePaymentCommandHandler yalnızca okur).
        if (appt is not null && appt.Status == AppointmentStatus.Scheduled)
        {
            var completion = appt.Complete();
            if (!completion.IsSuccess)
                return Result<ExaminationWriteResultDto>.Failure(completion.Error);

            await _appointmentsWrite.UpdateAsync(appt, ct);
        }

        await _examinationsWrite.SaveChangesAsync(ct);
        return Result<ExaminationWriteResultDto>.Success(
            new ExaminationWriteResultDto(examination.Id, ExaminationRowVersion.Encode(examination.RowVersion)));
    }
}
