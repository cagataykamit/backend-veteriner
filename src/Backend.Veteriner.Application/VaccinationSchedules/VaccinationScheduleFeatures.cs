using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.VaccinationSchedules;

/// <summary>Kliniğin aşı takvimi satırı (TIER-005; docs/VACCINATION_SCHEDULE_API_CONTRACT.md §2). Ad alanları okuma anındadır.</summary>
public sealed record VaccinationScheduleItemDto(
    Guid Id,
    Guid SpeciesId,
    string SpeciesName,
    Guid VaccineDefinitionId,
    string VaccineName,
    bool VaccineIsActive,
    int StartAgeWeeks,
    int DoseCount,
    int? DoseIntervalWeeks,
    int? RepeatIntervalMonths,
    string? Notes,
    DateTime? UpdatedAtUtc);

public sealed record VaccinationScheduleDto(IReadOnlyList<VaccinationScheduleItemDto> Items);

public sealed record VaccinationScheduleTemplateResultDto(int AddedCount, VaccinationScheduleDto Schedule);

/// <summary>Paketten bağımsız okunur (paket düşünce takvim silinmez).</summary>
public sealed record GetVaccinationScheduleQuery(Guid? SpeciesId) : IRequest<Result<VaccinationScheduleDto>>;

public sealed record CreateVaccinationScheduleItemCommand(
    Guid SpeciesId,
    Guid VaccineDefinitionId,
    int StartAgeWeeks,
    int DoseCount,
    int? DoseIntervalWeeks,
    int? RepeatIntervalMonths,
    string? Notes) : IRequest<Result<VaccinationScheduleItemDto>>, IAuditableRequest
{
    public string AuditAction => "VaccinationSchedule.Item.Create";
    public string? AuditTarget => $"SpeciesId={SpeciesId};VaccineDefinitionId={VaccineDefinitionId}";
}

/// <summary>Tür ve aşı değişmez; değiştirmek için satır silinip yenisi eklenir.</summary>
public sealed record UpdateVaccinationScheduleItemCommand(
    Guid Id,
    int StartAgeWeeks,
    int DoseCount,
    int? DoseIntervalWeeks,
    int? RepeatIntervalMonths,
    string? Notes) : IRequest<Result<VaccinationScheduleItemDto>>, IAuditableRequest
{
    public string AuditAction => "VaccinationSchedule.Item.Update";
    public string? AuditTarget => $"VaccinationScheduleItemId={Id}";
}

/// <summary>Takvim satırını siler; daha önce plandan oluşmuş aşı kayıtları etkilenmez.</summary>
public sealed record DeleteVaccinationScheduleItemCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "VaccinationSchedule.Item.Delete";
    public string? AuditTarget => $"VaccinationScheduleItemId={Id}";
}

/// <summary>Başlangıç şablonundan yalnız takvimde olmayan tür + aşı satırlarını ekler; mevcut satıra dokunmaz.</summary>
public sealed record LoadVaccinationScheduleStarterTemplateCommand : IRequest<Result<VaccinationScheduleTemplateResultDto>>, IAuditableRequest
{
    public string AuditAction => "VaccinationSchedule.StarterTemplate.Load";
    public string? AuditTarget => null;
}

public sealed class VaccinationScheduleItemsSpec : Specification<VaccinationScheduleItem>
{
    public VaccinationScheduleItemsSpec(Guid tenantId, Guid? speciesId)
    {
        Query.Where(x => x.TenantId == tenantId);
        if (speciesId.HasValue)
            Query.Where(x => x.SpeciesId == speciesId.Value);
    }
}

public sealed class VaccinationScheduleItemByIdSpec : Specification<VaccinationScheduleItem>
{
    public VaccinationScheduleItemByIdSpec(Guid tenantId, Guid id) => Query.Where(x => x.TenantId == tenantId && x.Id == id);
}

public sealed class VaccinationScheduleItemExistsSpec : Specification<VaccinationScheduleItem>
{
    public VaccinationScheduleItemExistsSpec(Guid tenantId, Guid speciesId, Guid vaccineDefinitionId)
        => Query.Where(x => x.TenantId == tenantId && x.SpeciesId == speciesId && x.VaccineDefinitionId == vaccineDefinitionId);
}

public sealed class CreateVaccinationScheduleItemCommandValidator : AbstractValidator<CreateVaccinationScheduleItemCommand>
{
    public CreateVaccinationScheduleItemCommandValidator()
    {
        RuleFor(x => x.SpeciesId).NotEmpty();
        RuleFor(x => x.VaccineDefinitionId).NotEmpty();
    }
}

public sealed class UpdateVaccinationScheduleItemCommandValidator : AbstractValidator<UpdateVaccinationScheduleItemCommand>
{
    public UpdateVaccinationScheduleItemCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeleteVaccinationScheduleItemCommandValidator : AbstractValidator<DeleteVaccinationScheduleItemCommand>
{
    public DeleteVaccinationScheduleItemCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Kiracı bağlamı ve paket kapısı (takvim yazma, öneri ve onay) tek yerde.</summary>
internal static class VaccinationWizardAccess
{
    public static Result<Guid> Tenant(ITenantContext tenantContext)
        => tenantContext.TenantId is { } tenantId
            ? Result<Guid>.Success(tenantId)
            : Result<Guid>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

    public static async Task<Result<Guid>> EnsureAllowedAsync(ITenantContext tenantContext, TenantPlanFeatureEvaluator planFeatures, CancellationToken ct)
    {
        var tenant = Tenant(tenantContext);
        if (!tenant.IsSuccess)
            return tenant;

        var allowed = await planFeatures.EnsureAllowedAsync(tenant.Value, PlanFeature.VaccinationWizard, ct);
        return allowed.IsSuccess ? tenant : Result<Guid>.Failure(allowed.Error);
    }
}

/// <summary>Satırları tür ve aşı adlarıyla döndürür: tür sırası, başlangıç yaşı, aşı adı.</summary>
public sealed class VaccinationScheduleReader
{
    private readonly IReadRepository<VaccinationScheduleItem> _items;
    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<VaccineDefinition> _definitions;

    public VaccinationScheduleReader(
        IReadRepository<VaccinationScheduleItem> items,
        IReadRepository<Species> species,
        IReadRepository<VaccineDefinition> definitions)
    {
        _items = items;
        _species = species;
        _definitions = definitions;
    }

    public async Task<VaccinationScheduleDto> ReadAsync(Guid tenantId, Guid? speciesId, CancellationToken ct)
        => await ToDtoAsync(await _items.ListAsync(new VaccinationScheduleItemsSpec(tenantId, speciesId), ct), ct);

    public async Task<VaccinationScheduleItemDto> ReadItemAsync(VaccinationScheduleItem item, CancellationToken ct)
        => (await ToDtoAsync([item], ct)).Items[0];

    private async Task<VaccinationScheduleDto> ToDtoAsync(IReadOnlyCollection<VaccinationScheduleItem> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return new VaccinationScheduleDto([]);

        var species = (await _species.ListAsync(new SpeciesByIdsSpec(rows.Select(r => r.SpeciesId).Distinct().ToArray()), ct))
            .ToDictionary(s => s.Id);
        var definitions = (await _definitions.ListAsync(new VaccineDefinitionsByIdsSpec(rows.Select(r => r.VaccineDefinitionId).Distinct().ToArray()), ct))
            .ToDictionary(d => d.Id);

        return new VaccinationScheduleDto(rows
            .Select(r =>
            {
                species.TryGetValue(r.SpeciesId, out var s);
                definitions.TryGetValue(r.VaccineDefinitionId, out var d);
                return (Order: s?.DisplayOrder ?? int.MaxValue, Dto: new VaccinationScheduleItemDto(
                    r.Id, r.SpeciesId, s?.Name ?? string.Empty, r.VaccineDefinitionId, d?.Name ?? string.Empty, d?.IsActive ?? false,
                    r.StartAgeWeeks, r.DoseCount, r.DoseIntervalWeeks, r.RepeatIntervalMonths, r.Notes, r.UpdatedAtUtc));
            })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Dto.SpeciesName, StringComparer.CurrentCulture)
            .ThenBy(x => x.Dto.StartAgeWeeks)
            .ThenBy(x => x.Dto.VaccineName, StringComparer.CurrentCulture)
            .Select(x => x.Dto)
            .ToArray());
    }
}

public sealed class GetVaccinationScheduleQueryHandler : IRequestHandler<GetVaccinationScheduleQuery, Result<VaccinationScheduleDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly VaccinationScheduleReader _reader;

    public GetVaccinationScheduleQueryHandler(ITenantContext tenantContext, VaccinationScheduleReader reader)
    {
        _tenantContext = tenantContext;
        _reader = reader;
    }

    public async ValueTask<Result<VaccinationScheduleDto>> Handle(GetVaccinationScheduleQuery request, CancellationToken ct)
    {
        var tenant = VaccinationWizardAccess.Tenant(_tenantContext);
        if (!tenant.IsSuccess)
            return Result<VaccinationScheduleDto>.Failure(tenant.Error);

        return Result<VaccinationScheduleDto>.Success(await _reader.ReadAsync(tenant.Value, request.SpeciesId, ct));
    }
}

public sealed class CreateVaccinationScheduleItemCommandHandler
    : IRequestHandler<CreateVaccinationScheduleItemCommand, Result<VaccinationScheduleItemDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<VaccineDefinition> _definitions;
    private readonly IRepository<VaccinationScheduleItem> _items;
    private readonly VaccinationScheduleReader _reader;
    private readonly TimeProvider _timeProvider;

    public CreateVaccinationScheduleItemCommandHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IReadRepository<Species> species,
        IReadRepository<VaccineDefinition> definitions,
        IRepository<VaccinationScheduleItem> items,
        VaccinationScheduleReader reader,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _species = species;
        _definitions = definitions;
        _items = items;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VaccinationScheduleItemDto>> Handle(CreateVaccinationScheduleItemCommand request, CancellationToken ct)
    {
        var tenant = await VaccinationWizardAccess.EnsureAllowedAsync(_tenantContext, _planFeatures, ct);
        if (!tenant.IsSuccess)
            return Result<VaccinationScheduleItemDto>.Failure(tenant.Error);
        var tenantId = tenant.Value;

        var species = await _species.FirstOrDefaultAsync(new SpeciesByIdSpec(request.SpeciesId), ct);
        if (species is not { IsActive: true })
            return Result<VaccinationScheduleItemDto>.Failure("VaccinationSchedules.InvalidSpecies", "Geçersiz tür seçildi.");

        var definition = await VaccinationCatalogResolver.ResolveActiveForSpeciesAsync(
            tenantId, request.VaccineDefinitionId, request.SpeciesId, _definitions, ct);
        if (!definition.IsSuccess)
            return Result<VaccinationScheduleItemDto>.Failure(definition.Error);

        if (await _items.AnyAsync(new VaccinationScheduleItemExistsSpec(tenantId, request.SpeciesId, request.VaccineDefinitionId), ct))
        {
            return Result<VaccinationScheduleItemDto>.Failure(
                "VaccinationSchedules.DuplicateItem", "Bu tür için bu aşı takvimde zaten var; mevcut satırı düzenleyin.");
        }

        var created = VaccinationScheduleItem.Create(
            tenantId, request.SpeciesId, request.VaccineDefinitionId, request.StartAgeWeeks, request.DoseCount,
            request.DoseIntervalWeeks, request.RepeatIntervalMonths, request.Notes, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<VaccinationScheduleItemDto>.Failure(created.Error);

        await _items.AddAsync(created.Value!, ct);
        await _items.SaveChangesAsync(ct);
        return Result<VaccinationScheduleItemDto>.Success(await _reader.ReadItemAsync(created.Value!, ct));
    }
}

public sealed class UpdateVaccinationScheduleItemCommandHandler
    : IRequestHandler<UpdateVaccinationScheduleItemCommand, Result<VaccinationScheduleItemDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<VaccinationScheduleItem> _items;
    private readonly VaccinationScheduleReader _reader;
    private readonly TimeProvider _timeProvider;

    public UpdateVaccinationScheduleItemCommandHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<VaccinationScheduleItem> items,
        VaccinationScheduleReader reader,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _items = items;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VaccinationScheduleItemDto>> Handle(UpdateVaccinationScheduleItemCommand request, CancellationToken ct)
    {
        var tenant = await VaccinationWizardAccess.EnsureAllowedAsync(_tenantContext, _planFeatures, ct);
        if (!tenant.IsSuccess)
            return Result<VaccinationScheduleItemDto>.Failure(tenant.Error);

        var item = await _items.FirstOrDefaultAsync(new VaccinationScheduleItemByIdSpec(tenant.Value, request.Id), ct);
        if (item is null)
            return Result<VaccinationScheduleItemDto>.Failure("VaccinationSchedules.NotFound", "Takvim satırı bulunamadı.");

        var updated = item.Update(
            request.StartAgeWeeks, request.DoseCount, request.DoseIntervalWeeks, request.RepeatIntervalMonths, request.Notes,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!updated.IsSuccess)
            return Result<VaccinationScheduleItemDto>.Failure(updated.Error);

        await _items.SaveChangesAsync(ct);
        return Result<VaccinationScheduleItemDto>.Success(await _reader.ReadItemAsync(item, ct));
    }
}

public sealed class DeleteVaccinationScheduleItemCommandHandler : IRequestHandler<DeleteVaccinationScheduleItemCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<VaccinationScheduleItem> _items;

    public DeleteVaccinationScheduleItemCommandHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<VaccinationScheduleItem> items)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _items = items;
    }

    public async ValueTask<Result> Handle(DeleteVaccinationScheduleItemCommand request, CancellationToken ct)
    {
        var tenant = await VaccinationWizardAccess.EnsureAllowedAsync(_tenantContext, _planFeatures, ct);
        if (!tenant.IsSuccess)
            return Result.Failure(tenant.Error);

        var item = await _items.FirstOrDefaultAsync(new VaccinationScheduleItemByIdSpec(tenant.Value, request.Id), ct);
        if (item is null)
            return Result.Failure("VaccinationSchedules.NotFound", "Takvim satırı bulunamadı.");

        await _items.DeleteAsync(item, ct);
        await _items.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class LoadVaccinationScheduleStarterTemplateCommandHandler
    : IRequestHandler<LoadVaccinationScheduleStarterTemplateCommand, Result<VaccinationScheduleTemplateResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<VaccineDefinition> _definitions;
    private readonly IRepository<VaccinationScheduleItem> _items;
    private readonly VaccinationScheduleReader _reader;
    private readonly TimeProvider _timeProvider;

    public LoadVaccinationScheduleStarterTemplateCommandHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IReadRepository<Species> species,
        IReadRepository<VaccineDefinition> definitions,
        IRepository<VaccinationScheduleItem> items,
        VaccinationScheduleReader reader,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _species = species;
        _definitions = definitions;
        _items = items;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<VaccinationScheduleTemplateResultDto>> Handle(LoadVaccinationScheduleStarterTemplateCommand request, CancellationToken ct)
    {
        var tenant = await VaccinationWizardAccess.EnsureAllowedAsync(_tenantContext, _planFeatures, ct);
        if (!tenant.IsSuccess)
            return Result<VaccinationScheduleTemplateResultDto>.Failure(tenant.Error);
        var tenantId = tenant.Value;

        var speciesIdByCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var code in VaccinationScheduleStarterTemplate.Rows.Select(r => r.SpeciesCode).Distinct())
        {
            if (await _species.FirstOrDefaultAsync(new SpeciesByCodeSpec(code), ct) is { IsActive: true } species)
                speciesIdByCode[code] = species.Id;
        }

        var definitionIdByCode = (await _definitions.ListAsync(
                new GlobalActiveVaccineDefinitionsByCodesSpec(VaccinationScheduleStarterTemplate.Rows.Select(r => r.VaccineCode).Distinct().ToArray()), ct))
            .ToDictionary(d => d.Code, d => d.Id, StringComparer.Ordinal);

        var existing = (await _items.ListAsync(new VaccinationScheduleItemsSpec(tenantId, speciesId: null), ct))
            .Select(i => (i.SpeciesId, i.VaccineDefinitionId))
            .ToHashSet();

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var added = 0;
        foreach (var row in VaccinationScheduleStarterTemplate.Rows)
        {
            // Sistem kataloğunda olmayan tür ya da aşı (silinmiş/pasif) atlanır; klinik eksik satırı elle ekler.
            if (!speciesIdByCode.TryGetValue(row.SpeciesCode, out var speciesId)
                || !definitionIdByCode.TryGetValue(row.VaccineCode, out var definitionId)
                || !existing.Add((speciesId, definitionId)))
            {
                continue;
            }

            var item = VaccinationScheduleItem.Create(
                tenantId, speciesId, definitionId, row.StartAgeWeeks, row.DoseCount, row.DoseIntervalWeeks, row.RepeatIntervalMonths,
                notes: null, utcNow);
            await _items.AddAsync(item.Value!, ct);
            added++;
        }

        if (added > 0)
            await _items.SaveChangesAsync(ct);

        return Result<VaccinationScheduleTemplateResultDto>.Success(
            new VaccinationScheduleTemplateResultDto(added, await _reader.ReadAsync(tenantId, speciesId: null, ct)));
    }
}
