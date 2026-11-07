using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Vaccinations.Commands.Create;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.VaccinationSchedules;

/// <summary>Takvim satırı için neden öneri çıkmadı (docs/VACCINATION_SCHEDULE_API_CONTRACT.md §3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VaccinationPlanSkipReason>))]
public enum VaccinationPlanSkipReason
{
    /// <summary>Bu aşının planlı (henüz uygulanmamış) kaydı var.</summary>
    AlreadyScheduled = 0,

    /// <summary>Son uygulanan dozda ileri tarihli sonraki uygulama tarihi girilmiş; hatırlatma o kayıttan gider.</summary>
    NextDueSet = 1,

    /// <summary>Temel seri bitti ve takvimde tekrar yok.</summary>
    Completed = 2,

    /// <summary>Aşı tanımı pasif.</summary>
    VaccineInactive = 3,

    /// <summary>Kalan doz planlanabilecek en uzak tarihten (5 yıl) sonraya düşüyor.</summary>
    BeyondHorizon = 4,
}

/// <summary><see cref="DoseNumber"/> <c>null</c>: tekrar dozu.</summary>
public sealed record VaccinationPlanSuggestionDto(
    Guid ScheduleItemId,
    Guid VaccineDefinitionId,
    string VaccineName,
    int? DoseNumber,
    int DoseCount,
    int AppliedDoseCount,
    DateOnly DueDate,
    bool IsLate);

public sealed record VaccinationPlanSkippedDto(
    Guid ScheduleItemId,
    Guid VaccineDefinitionId,
    string VaccineName,
    VaccinationPlanSkipReason Reason,
    DateOnly? DueDate);

public sealed record VaccinationPlanDto(
    Guid PetId,
    string PetName,
    Guid SpeciesId,
    string? SpeciesName,
    DateOnly? BirthDate,
    int ScheduleItemCount,
    IReadOnlyList<VaccinationPlanSuggestionDto> Suggestions,
    IReadOnlyList<VaccinationPlanSkippedDto> Skipped);

/// <summary>Takvimden önerilen plan; kayıt oluşturmaz.</summary>
public sealed record GetVaccinationPlanQuery(Guid PetId) : IRequest<Result<VaccinationPlanDto>>;

public sealed record VaccinationPlanApplyItem(Guid VaccineDefinitionId, DateOnly DueDate, string? Notes);

/// <summary>
/// Hekimin onayladığı (gerekirse tarihini değiştirdiği) dozları mevcut planlı aşı kaydı olarak yazar: her satır
/// <see cref="CreateVaccinationCommand"/> (<see cref="VaccinationStatus.Scheduled"/>) ile, hepsi tek transaction'da.
/// </summary>
public sealed record ApplyVaccinationPlanCommand(Guid PetId, Guid ClinicId, IReadOnlyList<VaccinationPlanApplyItem>? Items)
    : IRequest<Result<VaccinationPlanApplyResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "VaccinationPlan.Apply";
    public string? AuditTarget => $"PetId={PetId}";
}

public sealed record VaccinationPlanAppliedDto(Guid VaccinationId, Guid VaccineDefinitionId, DateOnly DueDate);

public sealed record VaccinationPlanApplyResultDto(Guid PetId, IReadOnlyList<VaccinationPlanAppliedDto> Created);

internal sealed record VaccinationPlanHistoryRow(
    Guid VaccineDefinitionId,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc);

/// <summary>Hayvanın iptal edilmemiş, katalog tanımlı aşı kayıtları; klinik kapsamı okuma kuralıyla (boş: kiracı geneli).</summary>
internal sealed class VaccinationPlanHistorySpec : Specification<Vaccination, VaccinationPlanHistoryRow>
{
    public VaccinationPlanHistorySpec(Guid tenantId, Guid petId, IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && v.PetId == petId && v.VaccineDefinitionId != null && v.Status != VaccinationStatus.Cancelled);
        if (accessibleClinicIds is not null)
            Query.Where(v => accessibleClinicIds.Contains(v.ClinicId));
        Query.Select(v => new VaccinationPlanHistoryRow(v.VaccineDefinitionId!.Value, v.Status, v.AppliedAtUtc, v.DueAtUtc));
    }
}

public sealed class ApplyVaccinationPlanCommandValidator : AbstractValidator<ApplyVaccinationPlanCommand>
{
    public const int MaxItems = 20;

    public ApplyVaccinationPlanCommandValidator()
    {
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.Items!.Count).LessThanOrEqualTo(MaxItems).When(x => x.Items is not null)
            .WithMessage($"Tek seferde en fazla {MaxItems} doz planlanabilir.");
        RuleFor(x => x.Items)
            .Must(items => items!.Select(i => (i.VaccineDefinitionId, i.DueDate)).Distinct().Count() == items!.Count)
            .When(x => x.Items is { Count: > 0 })
            .WithMessage("Aynı aşı aynı güne iki kez planlanamaz.");
        RuleForEach(x => x.Items).ChildRules(item => item.RuleFor(i => i.VaccineDefinitionId).NotEmpty());
    }
}

public sealed class GetVaccinationPlanQueryValidator : AbstractValidator<GetVaccinationPlanQuery>
{
    public GetVaccinationPlanQueryValidator() => RuleFor(x => x.PetId).NotEmpty();
}

internal static class VaccinationPlanPet
{
    public static async Task<Result<Pet>> LoadReceivingCareAsync(IReadRepository<Pet> pets, Guid tenantId, Guid petId, CancellationToken ct)
    {
        var pet = await pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, petId), ct);
        if (pet is null)
            return Result<Pet>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        // Vefat eden hayvana plan önerilmez (ADR-020 S5; Pet.ReceivesCare; kod Pet.EnsureCanStartCare ile aynı).
        return pet.IsReceivingCare
            ? Result<Pet>.Success(pet)
            : Result<Pet>.Failure("Pets.Deceased", "Vefat etmiş hayvan için aşı planı yapılamaz.");
    }
}

public sealed class GetVaccinationPlanQueryHandler : IRequestHandler<GetVaccinationPlanQuery, Result<VaccinationPlanDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<VaccinationScheduleItem> _items;
    private readonly IReadRepository<VaccineDefinition> _definitions;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly TimeProvider _timeProvider;

    public GetVaccinationPlanQueryHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Pet> pets,
        IReadRepository<VaccinationScheduleItem> items,
        IReadRepository<VaccineDefinition> definitions,
        IReadRepository<Vaccination> vaccinations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _clinicScopeResolver = clinicScopeResolver;
        _pets = pets;
        _items = items;
        _definitions = definitions;
        _vaccinations = vaccinations;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VaccinationPlanDto>> Handle(GetVaccinationPlanQuery request, CancellationToken ct)
    {
        var tenant = await VaccinationWizardAccess.EnsureAllowedAsync(_tenantContext, _planFeatures, ct);
        if (!tenant.IsSuccess)
            return Result<VaccinationPlanDto>.Failure(tenant.Error);
        var tenantId = tenant.Value;

        var loaded = await VaccinationPlanPet.LoadReceivingCareAsync(_pets, tenantId, request.PetId, ct);
        if (!loaded.IsSuccess)
            return Result<VaccinationPlanDto>.Failure(loaded.Error);
        var pet = loaded.Value!;

        // Uygulanan doz, kullanıcının okuyabildiği kliniklerden sayılır (aşı listesiyle aynı okuma kuralı).
        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, requestClinicId: null, ct);
        if (!scope.IsSuccess)
            return Result<VaccinationPlanDto>.Failure(scope.Error);

        var items = await _items.ListAsync(new VaccinationScheduleItemsSpec(tenantId, pet.SpeciesId), ct);
        var definitions = items.Count == 0
            ? new Dictionary<Guid, VaccineDefinition>()
            : (await _definitions.ListAsync(new VaccineDefinitionsByIdsSpec(items.Select(i => i.VaccineDefinitionId).ToArray()), ct))
                .ToDictionary(d => d.Id);
        var history = items.Count == 0
            ? []
            : await _vaccinations.ListAsync(new VaccinationPlanHistorySpec(tenantId, pet.Id, scope.Value!.AccessibleClinicIds), ct);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var earliest = OperationDayBounds.ToLocalDate(utcNow).AddDays(1);
        var horizon = OperationDayBounds.ToLocalDate(utcNow).AddYears(VaccinationDueAtWindow.MaxFutureYears);

        var suggestions = new List<VaccinationPlanSuggestionDto>();
        var skipped = new List<VaccinationPlanSkippedDto>();
        foreach (var item in items.OrderBy(i => i.StartAgeWeeks).ThenBy(i => definitions.GetValueOrDefault(i.VaccineDefinitionId)?.Name, StringComparer.CurrentCulture))
        {
            var definition = definitions.GetValueOrDefault(item.VaccineDefinitionId);
            var name = definition?.Name ?? string.Empty;
            void Skip(VaccinationPlanSkipReason reason, DateTime? atUtc = null)
                => skipped.Add(new(item.Id, item.VaccineDefinitionId, name, reason, atUtc.HasValue ? OperationDayBounds.ToLocalDate(atUtc.Value) : null));

            if (definition is not { IsActive: true })
            {
                Skip(VaccinationPlanSkipReason.VaccineInactive);
                continue;
            }

            var records = history.Where(h => h.VaccineDefinitionId == item.VaccineDefinitionId).ToList();
            if (records.Where(r => r.Status == VaccinationStatus.Scheduled).MinBy(r => r.DueAtUtc) is { } scheduled)
            {
                Skip(VaccinationPlanSkipReason.AlreadyScheduled, scheduled.DueAtUtc);
                continue;
            }

            var applied = records.Where(r => r.Status == VaccinationStatus.Applied && r.AppliedAtUtc.HasValue).ToList();
            if (applied.MaxBy(r => r.AppliedAtUtc) is { DueAtUtc: { } nextDue } && nextDue > utcNow)
            {
                Skip(VaccinationPlanSkipReason.NextDueSet, nextDue);
                continue;
            }

            var appliedDates = applied.Select(r => OperationDayBounds.ToLocalDate(r.AppliedAtUtc!.Value)).ToArray();
            var doses = item.PlanDoses(pet.BirthDate, appliedDates, earliest);
            if (doses.Count == 0)
            {
                Skip(VaccinationPlanSkipReason.Completed);
                continue;
            }

            var withinHorizon = doses.Where(d => d.DueDate <= horizon).ToList();
            if (withinHorizon.Count == 0)
            {
                Skip(VaccinationPlanSkipReason.BeyondHorizon);
                continue;
            }

            suggestions.AddRange(withinHorizon.Select(d => new VaccinationPlanSuggestionDto(
                item.Id, item.VaccineDefinitionId, name, d.DoseNumber, item.DoseCount, appliedDates.Length, d.DueDate, d.IsLate)));
        }

        return Result<VaccinationPlanDto>.Success(new VaccinationPlanDto(
            pet.Id,
            pet.Name,
            pet.SpeciesId,
            pet.Species?.Name,
            pet.BirthDate,
            items.Count,
            suggestions.OrderBy(s => s.DueDate).ThenBy(s => s.VaccineName, StringComparer.CurrentCulture).ToArray(),
            skipped));
    }
}

public sealed class ApplyVaccinationPlanCommandHandler : IRequestHandler<ApplyVaccinationPlanCommand, Result<VaccinationPlanApplyResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public ApplyVaccinationPlanCommandHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IReadRepository<Pet> pets,
        IReadRepository<Vaccination> vaccinations,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _pets = pets;
        _vaccinations = vaccinations;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VaccinationPlanApplyResultDto>> Handle(ApplyVaccinationPlanCommand request, CancellationToken ct)
    {
        var tenant = await VaccinationWizardAccess.EnsureAllowedAsync(_tenantContext, _planFeatures, ct);
        if (!tenant.IsSuccess)
            return Result<VaccinationPlanApplyResultDto>.Failure(tenant.Error);
        var tenantId = tenant.Value;

        var loaded = await VaccinationPlanPet.LoadReceivingCareAsync(_pets, tenantId, request.PetId, ct);
        if (!loaded.IsSuccess)
            return Result<VaccinationPlanApplyResultDto>.Failure(loaded.Error);

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        if (request.Items!.Any(i => i.DueDate <= today))
        {
            return Result<VaccinationPlanApplyResultDto>.Failure(
                "Vaccinations.ScheduledDueAtMustNotBePast", "Planlanan tarih bugünden sonra olmalıdır.");
        }

        // Çift tıklama ve yeniden gönderim: aynı aşının aynı güne planlı kaydı varsa hiçbir kayıt yazılmaz.
        var scheduled = (await _vaccinations.ListAsync(new VaccinationPlanHistorySpec(tenantId, request.PetId, accessibleClinicIds: null), ct))
            .Where(r => r.Status == VaccinationStatus.Scheduled && r.DueAtUtc.HasValue)
            .Select(r => (r.VaccineDefinitionId, OperationDayBounds.ToLocalDate(r.DueAtUtc!.Value)))
            .ToHashSet();
        if (request.Items!.Any(i => scheduled.Contains((i.VaccineDefinitionId, i.DueDate))))
        {
            return Result<VaccinationPlanApplyResultDto>.Failure(
                "VaccinationPlans.DuplicateScheduled", "Bu aşı o gün için zaten planlı; planı yenileyip tekrar deneyin.");
        }

        var created = new List<VaccinationPlanAppliedDto>();
        foreach (var item in request.Items!.OrderBy(i => i.DueDate))
        {
            var result = await _sender.Send(
                new CreateVaccinationCommand(
                    request.ClinicId,
                    request.PetId,
                    ExaminationId: null,
                    item.VaccineDefinitionId,
                    VaccinationStatus.Scheduled,
                    AppliedAtUtc: null,
                    OperationDayBounds.NoonUtcForLocalDate(item.DueDate),
                    item.Notes),
                ct);
            if (!result.IsSuccess)
                return Result<VaccinationPlanApplyResultDto>.Failure(result.Error);

            created.Add(new VaccinationPlanAppliedDto(result.Value!.Id, item.VaccineDefinitionId, item.DueDate));
        }

        return Result<VaccinationPlanApplyResultDto>.Success(new VaccinationPlanApplyResultDto(request.PetId, created));
    }
}
