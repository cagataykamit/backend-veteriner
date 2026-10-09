using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Examinations.Commands.Update;

public sealed class UpdateExaminationCommandHandler
    : IRequestHandler<UpdateExaminationCommand, Result<ExaminationWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IRepository<Examination> _examinationsWrite;

    public UpdateExaminationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IRepository<Examination> examinationsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinationsWrite = examinationsWrite;
    }

    public async Task<Result<ExaminationWriteResultDto>> Handle(UpdateExaminationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Examinations.Validation",
                "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<ExaminationWriteResultDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için muayene kaydı güncellenemez.");
        }

        var e = await _examinationsWrite.FirstOrDefaultAsync(
            new ExaminationForUpdateByIdSpec(tenantId, request.Id), ct);
        if (e is null)
            return Result<ExaminationWriteResultDto>.Failure("Examinations.NotFound", "Muayene kaydı bulunamadı.");

        var clinicAccess = await ExaminationClinicWriteScope.EnsureEntityAndTargetWriteAccessAsync(
            _clinicScopeResolver, tenantId, e.ClinicId, e.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ExaminationWriteResultDto>.Failure(clinicAccess.Error);

        if (_clinicContext.ClinicId is { } currentClinicId && e.ClinicId != currentClinicId)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Examinations.ClinicContextMismatch",
                "Muayene kaydı sadece aktif clinic bağlamında güncellenebilir.");
        }

        if (request.ClinicId is { } requestClinicId && requestClinicId != e.ClinicId)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Examinations.Validation",
                "Muayene kaydının kliniği değiştirilemez.");
        }

        if (request.PetId is { } requestPetId && requestPetId != e.PetId)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Examinations.Validation",
                "Muayene kaydının hastası değiştirilemez.");
        }

        if (request.AppointmentId is { } requestAppointmentId)
        {
            if (requestAppointmentId == Guid.Empty)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.Validation",
                    "AppointmentId gecersiz.");
            }

            if (e.AppointmentId != requestAppointmentId)
            {
                return Result<ExaminationWriteResultDto>.Failure(
                    "Examinations.AppointmentChangeNotAllowed",
                    "Randevu bağlantısı değiştirilemez veya kaldırılamaz.");
            }
        }

        var examinedUtc = ExaminationExaminedAtWindow.ToUtc(request.ExaminedAtUtc);
        var window = ExaminationExaminedAtWindow.Validate(examinedUtc);
        if (!window.IsSuccess)
            return Result<ExaminationWriteResultDto>.Failure(window.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, e.ClinicId), ct);
        if (clinic is null)
            return Result<ExaminationWriteResultDto>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, e.PetId), ct);
        if (pet is null)
            return Result<ExaminationWriteResultDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var domain = e.UpdateClinicalContent(
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
            request.VitalsMeasuredAtUtc);

        if (!domain.IsSuccess)
            return Result<ExaminationWriteResultDto>.Failure(domain.Error);

        e.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _examinationsWrite.UpdateAsync(e, ct);
            await _examinationsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ExaminationWriteResultDto>.Failure(
                "Examinations.ConcurrencyConflict",
                "Muayene kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.");
        }

        return Result<ExaminationWriteResultDto>.Success(
            new ExaminationWriteResultDto(e.Id, ExaminationRowVersion.Encode(e.RowVersion)));
    }
}
