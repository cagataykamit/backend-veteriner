using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Pets.Queries.GetSummary;

/// <summary>
/// Hasta özet kartı. Her bölüm bağımsızdır: izin yoksa <c>Forbidden</c>, kayıt yoksa <c>Empty</c>,
/// bölüm sorgusu hata verirse <c>Unavailable</c> (kısmi yanıt); diğer bölümler etkilenmez.
/// </summary>
public sealed class GetPetSummaryQueryHandler : IRequestHandler<GetPetSummaryQuery, Result<PetSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly ICurrentUserPermissionChecker _permissionChecker;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Visit> _visits;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetPetSummaryQueryHandler> _logger;

    public GetPetSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        ICurrentUserPermissionChecker permissionChecker,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Examination> examinations,
        IReadRepository<Visit> visits,
        TimeProvider timeProvider,
        ILogger<GetPetSummaryQueryHandler> logger)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _permissionChecker = permissionChecker;
        _pets = pets;
        _clinics = clinics;
        _hospitalizations = hospitalizations;
        _vaccinations = vaccinations;
        _examinations = examinations;
        _visits = visits;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<PetSummaryDto>> Handle(GetPetSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PetSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<PetSummaryDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId ?? _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PetSummaryDto>.Failure(scopeResult.Error);

        var clinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value.AccessibleClinicIds;
        var noClinicAccess = accessibleClinicIds is { Count: 0 };
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var hospitalization = await LoadAsync(PetRecordKind.Hospitalization, noClinicAccess, async () =>
        {
            var h = await _hospitalizations.FirstOrDefaultAsync(
                new ActiveHospitalizationForPetSummarySpec(tenantId, clinicId, request.PetId, accessibleClinicIds), ct);
            return h is null
                ? null
                : new PetSummaryActiveHospitalizationDto(
                    h.Id,
                    h.ClinicId,
                    await ClinicNameAsync(tenantId, h.ClinicId, ct),
                    h.AdmittedAtUtc,
                    h.PlannedDischargeAtUtc,
                    h.Reason,
                    h.ExaminationId);
        }, ct);

        var vaccinations = await LoadAsync(PetRecordKind.Vaccination, noClinicAccess, async () =>
        {
            var applied = await _vaccinations.FirstOrDefaultAsync(
                new LastAppliedVaccinationForPetSpec(tenantId, clinicId, request.PetId, accessibleClinicIds), ct);
            var due = await _vaccinations.FirstOrDefaultAsync(
                new NextDueVaccinationForPetSpec(tenantId, clinicId, request.PetId, accessibleClinicIds), ct);
            if (applied is null && due is null)
                return null;

            return new PetSummaryVaccinationsDto(
                applied is null
                    ? null
                    : new PetSummaryLastVaccinationDto(applied.Id, applied.VaccineName, applied.AppliedAtUtc!.Value, applied.ClinicId),
                due is null
                    ? null
                    : new PetSummaryNextVaccinationDto(
                        due.Id,
                        due.VaccineName,
                        due.DueAtUtc!.Value,
                        due.DueAtUtc.Value < nowUtc,
                        due.ClinicId));
        }, ct);

        var weight = await LoadAsync(PetRecordKind.Examination, noClinicAccess, async () =>
        {
            var e = await _examinations.FirstOrDefaultAsync(
                new LatestWeightExaminationForPetSpec(tenantId, clinicId, request.PetId, accessibleClinicIds), ct);
            return e is null
                ? null
                : new PetSummaryWeightDto(e.WeightKg!.Value, e.VitalsMeasuredAtUtc ?? e.ExaminedAtUtc, "Examination", e.Id);
        }, ct);

        var lastVisit = await LoadAsync(PetRecordKind.Visit, noClinicAccess, async () =>
        {
            var v = await _visits.FirstOrDefaultAsync(
                new LastVisitForPetSpec(tenantId, clinicId, request.PetId, accessibleClinicIds), ct);
            return v is null
                ? null
                : new PetSummaryLastVisitDto(
                    v.Id,
                    v.ArrivedAtUtc,
                    v.CareStatus,
                    v.ClinicId,
                    await ClinicNameAsync(tenantId, v.ClinicId, ct));
        }, ct);

        var lastExamination = await LoadAsync(PetRecordKind.Examination, noClinicAccess, async () =>
        {
            var rows = await _examinations.ListAsync(
                new ExaminationsForPetRecentSpec(tenantId, clinicId, request.PetId, 1, accessibleClinicIds), ct);
            var e = rows.FirstOrDefault();
            return e is null
                ? null
                : new PetSummaryLastExaminationDto(
                    e.Id,
                    e.ExaminedAtUtc,
                    e.VisitReason,
                    e.ClinicId,
                    await ClinicNameAsync(tenantId, e.ClinicId, ct));
        }, ct);

        return Result<PetSummaryDto>.Success(new PetSummaryDto(
            pet.Id,
            PetAlertsDto.From(pet.AlertFlags, pet.AlertNote),
            pet.Weight,
            hospitalization,
            vaccinations,
            weight,
            lastVisit,
            lastExamination));
    }

    private async Task<PetSummarySectionDto<T>> LoadAsync<T>(
        PetRecordKind kind,
        bool noClinicAccess,
        Func<Task<T?>> load,
        CancellationToken ct) where T : class
    {
        if (!_permissionChecker.CanRead(kind))
            return PetSummarySectionDto<T>.Forbidden();

        if (noClinicAccess)
            return PetSummarySectionDto<T>.Empty();

        try
        {
            var data = await load();
            return data is null ? PetSummarySectionDto<T>.Empty() : PetSummarySectionDto<T>.Ok(data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Hasta özeti bölümü okunamadı: {Kind}", kind);
            return PetSummarySectionDto<T>.Unavailable();
        }
    }

    private async Task<string> ClinicNameAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var rows = await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, [clinicId]), ct);
        return rows.FirstOrDefault()?.Name ?? string.Empty;
    }
}
