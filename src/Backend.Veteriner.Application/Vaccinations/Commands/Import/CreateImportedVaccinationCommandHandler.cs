using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Import;

/// <summary>
/// Kurallar mevcut sınıflardan gelir: tanım (aktif, kiracıya görünür, tür uyumlu) <see cref="VaccinationCatalogResolver"/>,
/// durum/tarih tutarlılığı <see cref="VaccinationStatusDateRules"/> (Applied dalı), vade gelecek sınırı
/// <see cref="VaccinationDueAtWindow"/>. Uygulama günü bugün olabilir (İstanbul 12:00 UTC'de henüz gelmemiş olabilir), bu
/// yüzden "gelecek" denetimi bir gün toleranslıdır (doğrulama motoruyla aynı).
/// </summary>
public sealed class CreateImportedVaccinationCommandHandler : IRequestHandler<CreateImportedVaccinationCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<VaccineDefinition> _vaccineDefinitions;
    private readonly IRepository<Vaccination> _vaccinations;
    private readonly TimeProvider _timeProvider;

    public CreateImportedVaccinationCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<VaccineDefinition> vaccineDefinitions,
        IRepository<Vaccination> vaccinations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _clinics = clinics;
        _pets = pets;
        _vaccineDefinitions = vaccineDefinitions;
        _vaccinations = vaccinations;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateImportedVaccinationCommand request, CancellationToken ct)
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
            return Result<Guid>.Failure("Tenants.TenantInactive", "Pasif kiracı için aşı kaydı oluşturulamaz.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var appliedUtc = VaccinationDueAtWindow.ToUtc(request.AppliedAtUtc);
        DateTime? dueUtc = request.DueAtUtc is { } due ? VaccinationDueAtWindow.ToUtc(due) : null;

        var statusDates = VaccinationStatusDateRules.Validate(VaccinationStatus.Applied, appliedUtc, dueUtc, now.AddDays(1));
        if (!statusDates.IsSuccess)
            return Result<Guid>.Failure(statusDates.Error);

        if (dueUtc is { } dueValue)
        {
            var window = VaccinationDueAtWindow.ValidateFutureLimit(dueValue, now);
            if (!window.IsSuccess)
                return Result<Guid>.Failure(window.Error);
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var definition = await VaccinationCatalogResolver.ResolveActiveForPetAsync(
            tenantId, request.VaccineDefinitionId, pet, _vaccineDefinitions, ct);
        if (!definition.IsSuccess)
            return Result<Guid>.Failure(definition.Error);

        var vaccination = Vaccination.CreateImported(
            tenantId,
            pet.Id,
            clinic.Id,
            definition.Value!.Id,
            definition.Value.Name,
            appliedUtc,
            dueUtc,
            request.Notes,
            request.ImportBatchId);

        await _vaccinations.AddAsync(vaccination, ct);
        await _vaccinations.SaveChangesAsync(ct);
        return Result<Guid>.Success(vaccination.Id);
    }
}
