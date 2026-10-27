using Backend.Veteriner.Application.Administrations.Commands.Create;
using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Administrations.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Vaccinations.Access;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Create;

/// <summary>
/// Aşı kaydını yazar; ürün verilmişse aynı transaction'da mevcut <see cref="CreateAdministrationCommand"/> ile stok düşümlü uygulamayı yazıp bağlar
/// (FEFO, süresi geçmiş parti, yetersiz bakiye kuralları tek yerde, uygulama handler'ında yaşar). Atomiklik komutun
/// <c>ITransactionalRollbackOnFailureRequest</c> işaretiyle sağlanır: uygulama adımı başarısız olursa aşı kaydı geri alınır.
/// </summary>
public sealed class CreateVaccinationCommandHandler : IRequestHandler<CreateVaccinationCommand, Result<VaccinationCreateResultDto>>
{
    /// <summary>Miktar verilmezse bir birim (doz/şişe; ürünün stok birimi).</summary>
    private const decimal DefaultQuantity = 1m;

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
    private readonly IReadRepository<Administration> _administrationsRead;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public CreateVaccinationCommandHandler(
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
        IReadRepository<Administration> administrationsRead,
        ISender sender,
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
        _administrationsRead = administrationsRead;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VaccinationCreateResultDto>> Handle(CreateVaccinationCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VaccinationCreateResultDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Result<VaccinationCreateResultDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
        {
            return Result<VaccinationCreateResultDto>.Failure(
                "Tenants.TenantInactive",
                "Pasif kiracı için aşı kaydı oluşturulamaz.");
        }

        if (request.ProductId.HasValue && request.Status != VaccinationStatus.Applied)
        {
            return Result<VaccinationCreateResultDto>.Failure(
                "Vaccinations.ProductRequiresAppliedStatus",
                "Ürün (stok düşümü) yalnız uygulanmış (Applied) aşı kaydıyla birlikte girilebilir.");
        }

        // Tekrar istek: aynı RequestId ilk kaydı döndürür, stok değişmez (tarih/muayene kuralları yeniden işletilmez).
        if (request.ProductId.HasValue && request.RequestId is { } requestId && requestId != Guid.Empty)
        {
            var existing = await _administrationsRead.FirstOrDefaultAsync(
                new AdministrationByRequestIdSpec(tenantId, requestId), ct);
            if (existing is not null)
                return await ReplayAsync(existing, request, tenantId, ct);
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
            return Result<VaccinationCreateResultDto>.Failure(statusDates.Error);

        if (appliedUtc.HasValue)
        {
            var aw = VaccinationAppliedAtWindow.Validate(appliedUtc.Value, utcNow);
            if (!aw.IsSuccess)
                return Result<VaccinationCreateResultDto>.Failure(aw.Error);
        }

        if (dueUtc.HasValue)
        {
            var dw = VaccinationDueAtWindow.Validate(dueUtc.Value, utcNow);
            if (!dw.IsSuccess)
                return Result<VaccinationCreateResultDto>.Failure(dw.Error);
        }

        if (_clinicContext.ClinicId.HasValue && request.ClinicId != _clinicContext.ClinicId.Value)
        {
            return Result<VaccinationCreateResultDto>.Failure(
                "Vaccinations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var effectiveClinicId = _clinicContext.ClinicId ?? request.ClinicId;

        var clinicAccess = await VaccinationClinicWriteScope.EnsureWriteAccessAsync(
            _clinicScopeResolver, tenantId, effectiveClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<VaccinationCreateResultDto>.Failure(clinicAccess.Error);

        var clinic = await _clinics.FirstOrDefaultAsync(
            new ClinicByIdSpec(tenantId, effectiveClinicId), ct);
        if (clinic is null)
            return Result<VaccinationCreateResultDto>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(
            new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<VaccinationCreateResultDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var defResult = await VaccinationCatalogResolver.ResolveActiveForPetAsync(
            tenantId,
            request.VaccineDefinitionId,
            pet,
            _vaccineDefinitions,
            ct);
        if (!defResult.IsSuccess)
            return Result<VaccinationCreateResultDto>.Failure(defResult.Error);

        var definition = defResult.Value!;

        if (request.ExaminationId is { } eid)
        {
            var exam = await _examinations.FirstOrDefaultAsync(
                new ExaminationByIdSpec(tenantId, eid), ct);
            if (exam is null)
            {
                return Result<VaccinationCreateResultDto>.Failure(
                    "Examinations.NotFound",
                    "Muayene bulunamadı veya kiracıya ait değil.");
            }

            if (exam.ClinicId != effectiveClinicId || exam.PetId != request.PetId)
            {
                return Result<VaccinationCreateResultDto>.Failure(
                    "Vaccinations.ExaminationPetClinicMismatch",
                    "Seçilen muayene ile klinik veya hayvan bilgisi uyuşmuyor.");
            }
        }

        var locked = await ExaminationLinkedRecordLock.EnsureOpenAsync(
            _examinations, tenantId, [request.ExaminationId], ct);
        if (!locked.IsSuccess)
            return Result<VaccinationCreateResultDto>.Failure(locked.Error);

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
            request.Notes,
            utcNow);

        await _vaccinationsWrite.AddAsync(vaccination, ct);
        await _vaccinationsWrite.SaveChangesAsync(ct);

        if (request.ProductId is null)
            return Result<VaccinationCreateResultDto>.Success(new(vaccination.Id, true, null, null));

        var administration = await SendAdministrationAsync(request, effectiveClinicId, appliedUtc, vaccination.Id, ct);
        if (!administration.IsSuccess)
            return Result<VaccinationCreateResultDto>.Failure(administration.Error);

        var created = administration.Value!;
        if (!created.Created)
        {
            // Aynı requestId ile eşzamanlı başka istek kazandı: bu isteğin aşı kaydı geri alınır (yetim kayıt kalmaz), yeniden deneme ilk kaydı döndürür.
            return Result<VaccinationCreateResultDto>.Failure(
                "Vaccinations.ConcurrencyConflict",
                "Aynı requestId ile eşzamanlı başka bir istek kazandı; aynı gövde ve requestId ile yeniden denemek güvenlidir.");
        }

        return Result<VaccinationCreateResultDto>.Success(
            new(vaccination.Id, true, created.Administration, created.Stock));
    }

    private async Task<Result<VaccinationCreateResultDto>> ReplayAsync(
        Administration existing, CreateVaccinationCommand request, Guid tenantId, CancellationToken ct)
    {
        var vaccination = existing.VaccinationId is { } linkedId
            ? await _vaccinationsRead.FirstOrDefaultAsync(new VaccinationByIdSpec(tenantId, linkedId), ct)
            : null;
        if (vaccination is null)
        {
            return Result<VaccinationCreateResultDto>.Failure(
                "Administrations.DuplicateRequestId",
                "Bu requestId başka bir uygulama isteği için kullanılmış.");
        }

        if (vaccination.PetId != request.PetId || vaccination.VaccineDefinitionId != request.VaccineDefinitionId)
        {
            return Result<VaccinationCreateResultDto>.Failure(
                "Vaccinations.DuplicateRequestId",
                "Bu requestId başka bir aşı isteği için kullanılmış.");
        }

        // Uygulama handler'ı eşleşmeyi (klinik/hasta/ürün/miktar/parti), iptal durumunu ve güncel stoğu değerlendirir.
        var replay = await SendAdministrationAsync(
            request, _clinicContext.ClinicId ?? request.ClinicId, request.AppliedAtUtc, vaccination.Id, ct);
        if (!replay.IsSuccess)
            return Result<VaccinationCreateResultDto>.Failure(replay.Error);

        var result = replay.Value!;
        return Result<VaccinationCreateResultDto>.Success(
            new(vaccination.Id, result.Created, result.Administration, result.Stock));
    }

    private ValueTask<Result<AdministrationCreateResultDto>> SendAdministrationAsync(
        CreateVaccinationCommand request, Guid clinicId, DateTime? administeredAtUtc, Guid vaccinationId, CancellationToken ct)
        => _sender.Send(
            new CreateAdministrationCommand(
                clinicId,
                request.PetId,
                request.ExaminationId,
                TreatmentId: null,
                request.ProductId!.Value,
                request.Quantity ?? DefaultQuantity,
                administeredAtUtc,
                Notes: null,
                LateEntryReason: null,
                request.RequestId,
                request.BatchId,
                vaccinationId),
            ct);
}
