using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.RecordHistory.Contracts.Dtos;
using Backend.Veteriner.Application.RecordHistory.Specs;
using Backend.Veteriner.Domain.RecordHistory;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.RecordHistory.Queries.GetRecordHistory;

/// <summary>
/// Geçmiş satırları kaydın kendisinden bağımsız saklanır; kayıt yoksa, silinmişse, geçmişi yoksa veya kullanıcının
/// kliniği dışındaysa boş liste döner (kiracı dışına sızıntı yok). Klinik kapsamı en son satırın kliniğiyle denetlenir.
/// </summary>
public sealed class GetRecordHistoryQueryHandler
    : IRequestHandler<GetRecordHistoryQuery, Result<IReadOnlyList<RecordChangeDto>>>
{
    public const int MaxRows = 200;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<RecordChange> _changes;
    private readonly IClinicVeterinarianReader _userNames;

    public GetRecordHistoryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<RecordChange> changes,
        IClinicVeterinarianReader userNames)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _changes = changes;
        _userNames = userNames;
    }

    public async ValueTask<Result<IReadOnlyList<RecordChangeDto>>> Handle(GetRecordHistoryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<RecordChangeDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<RecordChangeDto>>.Failure(scope.Error);

        var rows = await _changes.ListAsync(
            new RecordChangesByRecordSpec(tenantId, request.RecordType, request.RecordId, MaxRows), ct);
        if (rows.Count == 0 || !InScope(scope.Value!, rows[0].ClinicId))
            return Result<IReadOnlyList<RecordChangeDto>>.Success(Array.Empty<RecordChangeDto>());

        var names = await _userNames.GetNamesAsync(rows.Select(r => r.ActorUserId).Distinct().ToArray(), ct);
        IReadOnlyList<RecordChangeDto> items = rows
            .Select(r => new RecordChangeDto(
                r.Id,
                r.Kind,
                r.OccurredAtUtc,
                r.ActorUserId,
                names.TryGetValue(r.ActorUserId, out var name) ? name : null,
                r.Changes.Select(c => new RecordFieldChangeDto(c.Field, c.OldValue, c.NewValue)).ToList()))
            .ToList();
        return Result<IReadOnlyList<RecordChangeDto>>.Success(items);
    }

    private static bool InScope(ClinicReadScope scope, Guid clinicId)
        => scope.SingleClinicId is { } single
            ? single == clinicId
            : scope.AccessibleClinicIds is not { } accessible || accessible.Contains(clinicId);
}
