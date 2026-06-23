using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Access;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Create;

public sealed class CreateVaccinationCommandHandler : IRequestHandler<CreateVaccinationCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<VaccineDefinition> _vaccineDefinitions;
    private readonly IRepository<Vaccination> _vaccinationsWrite;

    public CreateVaccinationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<Examination> examinations,
        IReadRepository<VaccineDefinition> vaccineDefinitions,
        IRepository<Vaccination> vaccinationsWrite)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _examinations = examinations;
        _vaccineDefinitions = vaccineDefinitions;
        _vaccinationsWrite = vaccinationsWrite;
    }

    public async Task<Result<Guid>> Handle(CreateVaccinationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<Guid>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<Guid>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<Guid>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için aşı kaydı oluşturulamaz.");
        }

        DateTime? appliedUtc = request.AppliedAtUtc.HasValue
            ? VaccinationAppliedAtWindow.ToUtc(request.AppliedAtUtc.Value)
            : null;
        DateTime? dueUtc = request.DueAtUtc.HasValue
            ? VaccinationDueAtWindow.ToUtc(request.DueAtUtc.Value)
            : null;

        var statusDates = VaccinationStatusDateRules.Validate(request.Status, appliedUtc, dueUtc);
        if (!statusDates.IsSuccess)
            return Result<Guid>.Failure(statusDates.Error);

        if (appliedUtc.HasValue)
        {
            var aw = VaccinationAppliedAtWindow.Validate(appliedUtc.Value);
            if (!aw.IsSuccess)
                return Result<Guid>.Failure(aw.Error);
        }

        if (dueUtc.HasValue)
        {
            var dw = VaccinationDueAtWindow.Validate(dueUtc.Value);
            if (!dw.IsSuccess)
                return Result<Guid>.Failure(dw.Error);
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<Guid>.Failure(
                "Vaccinations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;

        var clinicAccess = await VaccinationClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, effectiveClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<Guid>.Failure(clinicAccess.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var defResult = await VaccinationCatalogResolver.ResolveActiveForPetAsync(
            tenantId,
            request.VaccineDefinitionId,
            pet,
            _vaccineDefinitions,
            ct);
        if (!defResult.IsSuccess)
            return Result<Guid>.Failure(defResult.Error);

        var definition = defResult.Value!;

        if (request.ExaminationId is { } eid)
        {
            var exam = await _examinations.FirstOrDefaultAsync(
                new ExaminationByIdSpec(tenantId, eid), ct);
            if (exam is null)
            {
                return Result<Guid>.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId || exam.PetId != request.PetId)
            {
                return Result<Guid>.Failure(
                    "Vaccinations.ExaminationPetClinicMismatch",
                    "Seçilen muayene ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }

        var vaccination = new Vaccination(
            tenantId,
            request.PetId,
            effectiveClinicId,
            request.ExaminationId,
            definition.Id,
            definition.Name,
            request.Status,
            appliedUtc,
            dueUtc,
            request.Notes);

        await _vaccinationsWrite.AddAsync(vaccination, ct);
        await _vaccinationsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(vaccination.Id);
    }
}
