using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Access;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Update;

public sealed class UpdateVaccinationCommandHandler : IRequestHandler<UpdateVaccinationCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<VaccineDefinition> _vaccineDefinitions;
    private readonly IReadRepository<Vaccination> _vaccinationsRead;
    private readonly IRepository<Vaccination> _vaccinationsWrite;
    private readonly IReadRepository<Administration> _administrations;
    private readonly TimeProvider _timeProvider;

    public UpdateVaccinationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<VaccineDefinition> vaccineDefinitions,
        IReadRepository<Vaccination> vaccinationsRead,
        IRepository<Vaccination> vaccinationsWrite,
        IReadRepository<Administration> administrations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _vaccineDefinitions = vaccineDefinitions;
        _vaccinationsRead = vaccinationsRead;
        _vaccinationsWrite = vaccinationsWrite;
        _administrations = administrations;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(UpdateVaccinationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için aşı kaydı güncellenemez.");
        }

        DateTime? appliedUtc = request.AppliedAtUtc.HasValue
            ? VaccinationAppliedAtWindow.ToUtc(request.AppliedAtUtc.Value)
            : null;
        DateTime? dueUtc = request.DueAtUtc.HasValue
            ? VaccinationDueAtWindow.ToUtc(request.DueAtUtc.Value)
            : null;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var statusDates = VaccinationStatusDateRules.Validate(request.Status, appliedUtc, dueUtc, utcNow);
        if (!statusDates.IsSuccess)
            return Result.Failure(statusDates.Error);

        if (appliedUtc.HasValue)
        {
            var aw = VaccinationAppliedAtWindow.Validate(appliedUtc.Value, utcNow);
            if (!aw.IsSuccess)
                return Result.Failure(aw.Error);
        }

        if (dueUtc.HasValue)
        {
            var dw = VaccinationDueAtWindow.Validate(dueUtc.Value, utcNow);
            if (!dw.IsSuccess)
                return Result.Failure(dw.Error);
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result.Failure(
                "Vaccinations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var v = await _vaccinationsRead.FirstOrDefaultAsync(
            new VaccinationByIdSpec(tenantId, request.Id), ct);
        if (v is null)
            return Result.Failure("Vaccinations.NotFound", "Aşı kaydı bulunamadı.");

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;
        if (effectiveClinicId == Guid.Empty)
            effectiveClinicId = v.ClinicId;

        var clinicAccess = await VaccinationClinicWriteScope.EnsureEntityAndTargetWriteAccessAsync(
            _clinicScopeResolver, tenantId, v.ClinicId, effectiveClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        if (_clinicContext.ClinicId is { } currentClinicId && effectiveClinicId != currentClinicId)
        {
            return Result.Failure(
                "Vaccinations.ClinicContextMismatch",
                "Aşı kaydı sadece aktif clinic bağlamında güncellenebilir.");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var defResult = await VaccinationCatalogResolver.ResolveActiveForPetAsync(
            tenantId,
            request.VaccineDefinitionId,
            pet,
            _vaccineDefinitions,
            ct);
        if (!defResult.IsSuccess)
            return Result.Failure(defResult.Error);

        var definition = defResult.Value!;

        if (request.ExaminationId is { } eid)
        {
            var exam = await _examinations.FirstOrDefaultAsync(
                new ExaminationByIdSpec(tenantId, eid), ct);
            if (exam is null)
            {
                return Result.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId || exam.PetId != request.PetId)
            {
                return Result.Failure(
                    "Vaccinations.ExaminationPetClinicMismatch",
                    "Seçilen muayene ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }

        var locked = await ExaminationLinkedRecordLock.EnsureOpenAsync(
            _examinations, tenantId, [request.ExaminationId, v.ExaminationId], ct);
        if (!locked.IsSuccess)
            return locked;

        if (await _administrations.AnyAsync(new AdministrationByVaccinationIdSpec(tenantId, v.Id), ct)
            && (v.PetId != request.PetId || v.ClinicId != effectiveClinicId || v.ExaminationId != request.ExaminationId
                || v.VaccineDefinitionId != definition.Id || v.Status != request.Status || v.AppliedAtUtc != appliedUtc))
        {
            return Result.Failure(
                "Vaccinations.LinkedToAdministration",
                "Uygulamaya (stok düşümü) bağlı aşı kaydında yalnız notlar ve sonraki doz tarihi değişebilir; iptal için uygulama iptal edilmelidir.");
        }

        var domain = v.UpdateDetails(
            request.PetId,
            effectiveClinicId,
            request.ExaminationId,
            definition.Id,
            definition.Name,
            request.Status,
            appliedUtc,
            dueUtc,
            request.Notes,
            utcNow);

        if (!domain.IsSuccess)
            return Result.Failure(domain.Error);

        await _vaccinationsWrite.UpdateAsync(v, ct);
        await _vaccinationsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }
}
