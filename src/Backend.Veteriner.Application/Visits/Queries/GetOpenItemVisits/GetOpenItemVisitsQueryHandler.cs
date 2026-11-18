using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Visits.Queries.GetOpenItemVisits;

/// <summary>
/// Açık iş listesi: Bugün özetiyle aynı klinik kapsamı, izin süzmesi ve pencere (<see cref="OpenItemsSummaryRequest.ForToday"/>);
/// kural <see cref="IVisitOpenItemsReader"/>'dadır, burada tekrar yazılmaz.
/// </summary>
public sealed class GetOpenItemVisitsQueryHandler : IRequestHandler<GetOpenItemVisitsQuery, Result<OpenItemVisitsDto>>
{
    /// <summary>Listede en çok satır; toplam sayı yine tam döner.</summary>
    public const int MaxItems = 200;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IVisitOpenItemsReader _openItems;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly TimeProvider _timeProvider;

    public GetOpenItemVisitsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IVisitOpenItemsReader openItems,
        ICurrentUserPermissionChecker permissions,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _openItems = openItems;
        _permissions = permissions;
        _veterinarians = veterinarians;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<OpenItemVisitsDto>> Handle(GetOpenItemVisitsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<OpenItemVisitsDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<OpenItemVisitsDto>.Failure(ClinicContextRule.Mismatch("Visits"));

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
        {
            return Result<OpenItemVisitsDto>.Failure(
                "Visits.ClinicScopeRequired",
                "Açık iş listesi için klinik belirlenemedi.");
        }

        if (!Enum.IsDefined(request.Kind))
            return Result<OpenItemVisitsDto>.Failure("Visits.Validation", "Geçersiz açık iş türü.");

        var clinicScope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!clinicScope.IsSuccess)
            return Result<OpenItemVisitsDto>.Failure(clinicScope.Error);

        var scope = OpenItemsScope.For(tenantId, clinicId, _permissions);
        // Özette bu sayı null döner (Charges.Read yok); liste de açılmaz.
        if (request.Kind == OpenItemVisitKind.Uncharged && !scope.Sees(OpenItemSources.Charges))
        {
            return Result<OpenItemVisitsDto>.Failure(
                "Visits.OpenItemsForbidden",
                "Ücret yok listesi için ücret okuma izni gerekir.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var data = await _openItems.ListVisitsAsync(OpenItemsSummaryRequest.ForToday(scope, nowUtc), request.Kind, MaxItems, ct);

        var responsibleIds = data.Rows.Where(r => r.ResponsibleVeterinarianUserId.HasValue)
            .Select(r => r.ResponsibleVeterinarianUserId!.Value).Distinct().ToList();
        var names = responsibleIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _veterinarians.GetNamesAsync(responsibleIds, ct);

        IReadOnlyDictionary<Guid, VisitOpenItems>? workByVisit = null;
        if (request.Kind == OpenItemVisitKind.Uncharged && data.Rows.Count > 0)
            workByVisit = await _openItems.GetForVisitsAsync(scope, data.Rows.Select(r => r.VisitId).ToList(), ct);

        var items = data.Rows
            .Select(r => new OpenItemVisitDto(
                r.VisitId,
                r.ArrivedAtUtc,
                r.CareStatus,
                r.PetId,
                r.PetName,
                r.SpeciesName,
                r.ClientId,
                r.ClientName,
                r.ResponsibleVeterinarianUserId,
                r.ResponsibleVeterinarianUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
                r.LatestExaminationId,
                workByVisit?.GetValueOrDefault(r.VisitId)?.ChargeableWorkCount))
            .ToList();

        return Result<OpenItemVisitsDto>.Success(new OpenItemVisitsDto(request.Kind, data.TotalCount, items));
    }
}
