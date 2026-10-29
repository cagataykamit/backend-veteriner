using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Pets.Timeline;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Pets.Queries.GetTimeline;

public sealed class GetPetTimelineQueryHandler
    : IRequestHandler<GetPetTimelineQuery, Result<PetTimelineDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly ICurrentUserPermissionChecker _permissionChecker;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadOnlyDictionary<PetRecordKind, IPetTimelineSource> _sources;

    public GetPetTimelineQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        ICurrentUserPermissionChecker permissionChecker,
        IReadRepository<Pet> pets,
        IReadRepository<Clinic> clinics,
        IReadRepository<Examination> examinations,
        IEnumerable<IPetTimelineSource> sources)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _permissionChecker = permissionChecker;
        _pets = pets;
        _clinics = clinics;
        _examinations = examinations;
        _sources = sources.ToDictionary(s => s.Kind);
    }

    public async ValueTask<Result<PetTimelineDto>> Handle(GetPetTimelineQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PetTimelineDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var parsed = ParseRequest(request);
        if (!parsed.IsSuccess)
            return Result<PetTimelineDto>.Failure(parsed.Error);

        var (requestedKinds, fromUtc, toUtc, limit, cursor) = parsed.Value!;

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<PetTimelineDto>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, request.ClinicId ?? _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PetTimelineDto>.Failure(scopeResult.Error);

        var (allowedKinds, omittedKinds) = _permissionChecker.Split(requestedKinds);
        var omittedTypes = omittedKinds.Select(k => k.ToString()).ToList();

        var scope = scopeResult.Value!;
        if (scope.AccessibleClinicIds is { Count: 0 } || allowedKinds.Count == 0)
            return Result<PetTimelineDto>.Success(new PetTimelineDto([], null, omittedTypes));

        var sourceQuery = new PetTimelineSourceQuery(
            tenantId,
            request.PetId,
            scope.SingleClinicId,
            scope.AccessibleClinicIds,
            fromUtc,
            toUtc,
            request.IncludeVoided,
            cursor,
            limit + 1);

        var lists = new List<IReadOnlyList<PetTimelineRow>>(allowedKinds.Count);
        foreach (var kind in allowedKinds)
            lists.Add(await _sources[kind].ReadAsync(sourceQuery, ct));

        var (page, hasMore) = PetTimelineMerger.Page(lists, limit);

        var visitExaminationIds = await ResolveVisitExaminationIdsAsync(page, tenantId, scope, ct);

        var clinicIds = page.Select(r => r.ClinicId).Distinct().ToArray();
        var clinicNameById = clinicIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, clinicIds), ct))
                .ToDictionary(c => c.Id, c => c.Name);

        var items = page
            .Select(r => new PetTimelineItemDto(
                r.Kind,
                r.SourceId,
                r.EventAtUtc,
                r.ClinicId,
                clinicNameById.GetValueOrDefault(r.ClinicId, string.Empty),
                r.Title,
                r.Summary,
                r.Status,
                r.IsVoided,
                new PetTimelineLinkRefDto(r.Kind, r.SourceId),
                r.Kind == PetRecordKind.Visit && visitExaminationIds.TryGetValue(r.SourceId, out var examinationId) ? examinationId : r.RelatedExaminationId,
                r.RelatedVisitId,
                r.RelatedAppointmentId,
                r.UpdatedAtUtc,
                r.AddedAfterFinalization,
                r.RelatedTreatmentId))
            .ToList();

        var nextCursor = hasMore && page.Count > 0 ? PetTimelineMerger.CursorOf(page[^1]).Encode() : null;
        return Result<PetTimelineDto>.Success(new PetTimelineDto(items, nextCursor, omittedTypes));
    }

    /// <summary>
    /// Geliş satırlarının bağlı muayenesi (birden fazlaysa en yenisi). Muayene okuma izni yoksa hiç sorgulanmaz;
    /// doldurma <c>types</c> süzgecinden bağımsızdır, klinik kapsamı muayene kaynağıyla aynıdır.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Guid>> ResolveVisitExaminationIdsAsync(
        IReadOnlyList<PetTimelineRow> page,
        Guid tenantId,
        ClinicReadScope scope,
        CancellationToken ct)
    {
        var visitIds = page.Where(r => r.Kind == PetRecordKind.Visit).Select(r => r.SourceId).ToArray();
        if (visitIds.Length == 0 || !_permissionChecker.CanRead(PetRecordKind.Examination))
            return new Dictionary<Guid, Guid>();

        var examinations = await _examinations.ListAsync(
            new ExaminationsByVisitIdsSpec(tenantId, visitIds, scope.SingleClinicId, scope.AccessibleClinicIds), ct);

        // Liste en yeniden eskiye sıralı; her geliş için ilk muayene kazanır.
        var result = new Dictionary<Guid, Guid>();
        foreach (var e in examinations)
            result.TryAdd(e.VisitId!.Value, e.Id);
        return result;
    }

    private static Result<ParsedRequest> ParseRequest(GetPetTimelineQuery request)
    {
        static Result<ParsedRequest> Invalid(string message)
            => Result<ParsedRequest>.Failure("Pets.Validation", message);

        var kinds = new List<PetRecordKind>();
        foreach (var name in request.Types ?? [])
        {
            if (!Enum.TryParse<PetRecordKind>(name, ignoreCase: true, out var kind)
                || !PetTimelineConstants.SupportedKinds.Contains(kind))
            {
                return Invalid($"Geçersiz timeline türü: {name}.");
            }

            if (!kinds.Contains(kind))
                kinds.Add(kind);
        }

        if (kinds.Count == 0)
            kinds.AddRange(PetTimelineConstants.SupportedKinds);

        var limit = request.Limit ?? PetTimelineConstants.DefaultLimit;
        if (limit < 1 || limit > PetTimelineConstants.MaxLimit)
            return Invalid($"limit 1 ile {PetTimelineConstants.MaxLimit} arasında olmalı.");

        var from = request.FromUtc.HasValue ? NormalizeUtc(request.FromUtc.Value) : (DateTime?)null;
        var to = request.ToUtc.HasValue ? NormalizeUtc(request.ToUtc.Value) : (DateTime?)null;
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
            return Invalid("fromUtc, toUtc değerinden önce olmalı.");

        PetTimelineCursor? cursor = null;
        if (request.Cursor is not null && !PetTimelineCursor.TryDecode(request.Cursor, out cursor))
            return Invalid("Geçersiz imleç.");

        return Result<ParsedRequest>.Success(
            new ParsedRequest(kinds, from, to, limit, cursor));
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private sealed record ParsedRequest(
        IReadOnlyList<PetRecordKind> Kinds,
        DateTime? FromUtc,
        DateTime? ToUtc,
        int Limit,
        PetTimelineCursor? Cursor);
}
