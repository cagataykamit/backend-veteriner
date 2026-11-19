using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
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
using Mediator;
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
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IClientContext _clientContext;
    private readonly IClinicVeterinarianReader _veterinarians;
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
        IRepository<Visit> visitsWrite,
        IClientContext clientContext,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _visitsWrite = visitsWrite;
        _clientContext = clientContext;
        _veterinarians = veterinarians;
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

    public async ValueTask<Result<ExaminationWriteResultDto>> Handle(CreateExaminationCommand request, CancellationToken ct)
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
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var window = ExaminationExaminedAtWindow.Validate(examinedUtc, utcNow);

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

            if (ClinicContextRule.Conflicts(requestClinicId, _clinicContext.ClinicId))
            {
                return Result<ExaminationWriteResultDto>.Failure(ClinicContextRule.Mismatch("Examinations"));
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
                    "Randevu seçilmediyse klinik ve hasta seçimi zorunludur.");
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

        var careGate = pet.EnsureCanStartCare();
        if (!careGate.IsSuccess)
            return Result<ExaminationWriteResultDto>.Failure(careGate.Error);

        // Gelişsiz açılan bugünkü muayene bir gelişe bağlanır (kullanıcı kararı 2026-11-19): hayvanın açık gelişi (aynı randevuyla)
        // varsa o, yoksa yeni geliş (randevulu ya da randevusuz) açılıp başlatılır. Böylece muayene Bugün'de görünür ve ücret
        // kontrolüne (Açık işler "Ücret yok") girer. Geçmiş tarihli muayene arşiv kaydıdır, geliş açmaz.
        var openedVisit = false;
        if (visit is null && OperationDayBounds.ToLocalDate(examinedUtc) == OperationDayBounds.ToLocalDate(utcNow))
        {
            var linked = await FindOpenVisitAsync(tenantId, clinicId, petId, appt?.Id, ct);
            if (linked is not null)
            {
                visit = linked;
            }
            else if (await CanOpenVisitAsync(tenantId, petId, appt?.Id, ct))
            {
                var userId = _clientContext.UserId;
                if (userId is not { } creatorId)
                    return Result<ExaminationWriteResultDto>.Failure("Auth.UserMissing", "Kullanıcı bilgisi yok.");

                visit = new Visit(
                    tenantId,
                    clinicId,
                    petId,
                    appt?.Id,
                    await ResponsibleVeterinarianAsync(tenantId, clinicId, creatorId, appt, ct),
                    creatorId,
                    examinedUtc < utcNow ? examinedUtc : utcNow);
                await _visitsWrite.AddAsync(visit, ct);
                openedVisit = true;
            }
        }

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
            utcNow,
            request.Anamnesis,
            request.Plan,
            request.WeightKg,
            request.TemperatureC,
            request.HeartRateBpm,
            request.RespiratoryRatePerMin,
            request.VitalsMeasuredAtUtc,
            visit?.Id);

        // Muayene başlayınca bekleyen geliş "Devam ediyor" olur.
        if (visit is { CareStatus: VisitCareStatus.Waiting })
        {
            var started = visit.Start(utcNow);
            if (!started.IsSuccess)
                return Result<ExaminationWriteResultDto>.Failure(started.Error);
        }

        // Appointment lifecycle: akış bir randevuya bağlıysa planlanmış randevu muayeneyle birlikte
        // otomatik tamamlanır. Completed/Cancelled durumları için mevcut kural korunur (no-op).
        // Ödeme akışı bu mantığa hiçbir zaman dokunmaz (CreatePaymentCommandHandler yalnızca okur).
        if (appt is not null && appt.Status == AppointmentStatus.Scheduled)
        {
            var completion = appt.Complete();
            if (!completion.IsSuccess)
                return Result<ExaminationWriteResultDto>.Failure(completion.Error);

            await _appointmentsWrite.UpdateAsync(appt, ct);
        }

        // Muayene, geliş ve randevu tek SaveChanges'te atomiktir.
        try
        {
            await _examinationsWrite.AddAsync(examination, ct);
            await _examinationsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) when (visit is not null)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Visits.ConcurrencyConflict",
                "Geliş kaydı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }
        catch (DbUpdateException) when (openedVisit)
        {
            // Eşzamanlı geliş açıldı (hayvan başına tek aktif geliş / randevu başına tek geliş indeksi): tekrar denenince ona bağlanır.
            return Result<ExaminationWriteResultDto>.Failure(
                "Visits.ConcurrencyConflict",
                "Aynı anda bu hasta için geliş açıldı; işlem tekrarlanmalı.");
        }

        return Result<ExaminationWriteResultDto>.Success(
            new ExaminationWriteResultDto(examination.Id, ExaminationRowVersion.Encode(examination.RowVersion)));
    }

    /// <summary>
    /// Bağlanacak açık geliş (izlenen kayıt): randevulu muayenede o randevunun açık gelişi, randevusuzda hayvanın aynı klinikteki
    /// randevusuz açık gelişi. Bulunamazsa <c>null</c>.
    /// </summary>
    private async Task<Visit?> FindOpenVisitAsync(Guid tenantId, Guid clinicId, Guid petId, Guid? appointmentId, CancellationToken ct)
    {
        var candidate = appointmentId is { } aid
            ? await _visits.FirstOrDefaultAsync(new VisitByAppointmentIdSpec(tenantId, aid), ct)
            : await _visits.FirstOrDefaultAsync(new ActiveVisitByPetIdSpec(tenantId, petId), ct);

        if (candidate is null
            || candidate.CareStatus == VisitCareStatus.Completed
            || candidate.ClinicId != clinicId
            || candidate.AppointmentId != appointmentId)
        {
            return null;
        }

        // Bekleyen geliş muayeneyle başlatılacağı için izlenen kayıt yüklenir.
        return await _visits.FirstOrDefaultAsync(new VisitByIdSpec(tenantId, candidate.Id), ct);
    }

    /// <summary>Yeni geliş açılabilir mi: hayvanın başka açık gelişi ve (randevuluysa) randevunun gelişi yoksa.</summary>
    private async Task<bool> CanOpenVisitAsync(Guid tenantId, Guid petId, Guid? appointmentId, CancellationToken ct)
    {
        if (await _visits.FirstOrDefaultAsync(new ActiveVisitByPetIdSpec(tenantId, petId), ct) is not null)
            return false;

        return appointmentId is not { } aid
               || await _visits.FirstOrDefaultAsync(new VisitByAppointmentIdSpec(tenantId, aid), ct) is null;
    }

    /// <summary>Muayeneyi yazan klinikte aktif hekimse o; değilse randevunun (aktif) hekimi; ikisi de yoksa hekimsiz.</summary>
    private async Task<Guid?> ResponsibleVeterinarianAsync(Guid tenantId, Guid clinicId, Guid userId, Appointment? appointment, CancellationToken ct)
    {
        if (await _veterinarians.IsClinicVeterinarianAsync(userId, tenantId, clinicId, ct))
            return userId;

        return appointment?.VeterinarianUserId is { } vetId && await _veterinarians.IsClinicVeterinarianAsync(vetId, tenantId, clinicId, ct)
            ? vetId
            : null;
    }
}
