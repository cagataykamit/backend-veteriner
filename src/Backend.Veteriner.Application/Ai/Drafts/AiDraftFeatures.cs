using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai.Drafts;

// AI taslağı inceleme ve karar uçları (AI-009; docs/AI_DRAFTS_API_CONTRACT.md). Taslak klinik veridir: aynı kiracı, aktif
// klinik (taslağın kliniği), Ai.Use ve özelliğin uygulayıcısındaki izin gerekir. Kabul hedef kayda yalnız uygulayıcı
// üzerinden (mevcut komutlarla) yazar; kabul edilmemiş taslak hiçbir kayda yazılmaz (ADR-008 madde 3).

/// <summary>AI taslağı. <see cref="Content"/> yalnız karar bekleyen taslakta doludur.</summary>
/// <param name="Status">Okuma anındaki durum (süresi geçmiş taslak <c>Expired</c> görünür).</param>
/// <param name="Sources">Üretimde modele gönderilen kayıtlar (tür + kimlik).</param>
/// <param name="AppliedRecordId">Kabulde yazılan kayıt.</param>
public sealed record AiDraftDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AiFeature Feature,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AiDraftStatus Status,
    string TargetType,
    Guid TargetId,
    IReadOnlyList<AiDraftSourceDto> Sources,
    JsonObject? Content,
    string Model,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    Guid? AppliedRecordId)
{
    public static AiDraftDto From(AiDraft draft, DateTime utcNow)
    {
        var status = draft.StatusAt(utcNow);
        return new AiDraftDto(
            draft.Id,
            draft.Feature,
            status,
            draft.TargetType,
            draft.TargetId,
            draft.SourceRefs.Select(s => new AiDraftSourceDto(s.Type, s.Id)).ToList(),
            status == AiDraftStatus.Draft && draft.Content is { } content ? JsonNode.Parse(content) as JsonObject : null,
            draft.Model,
            draft.CreatedByUserId,
            draft.CreatedAtUtc,
            draft.ExpiresAtUtc,
            draft.DecidedByUserId,
            draft.DecidedAtUtc,
            draft.AppliedRecordId);
    }
}

/// <summary>Taslağın dayandığı kayıt; istemci <see cref="Type"/> → rota eşlemesiyle kayda bağlanır.</summary>
public sealed record AiDraftSourceDto(string Type, Guid Id);

// ----- Erişim (tek yer) -----

/// <summary>
/// Taslağa erişim kuralının tek yeri: kiracı + kullanıcı bağlamı, aktif klinik ve kullanıcının o kliniğe erişimi, taslağın
/// o klinikte olması, özelliğin uygulayıcısı ve uygulayıcının izni. Başka klinikteki taslak "bulunamadı" döner.
/// </summary>
public sealed class AiDraftAccess
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IEnumerable<IAiDraftApplier> _appliers;
    private readonly IRepository<AiDraft> _drafts;

    public AiDraftAccess(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        ICurrentUserPermissionChecker permissions,
        IEnumerable<IAiDraftApplier> appliers,
        IRepository<AiDraft> drafts)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _permissions = permissions;
        _appliers = appliers;
        _drafts = drafts;
    }

    /// <summary>İstek bağlamı: kiracı, aktif klinik (erişimi doğrulanmış) ve kullanıcı.</summary>
    public async Task<Result<AiDraftCaller>> ResolveCallerAsync(CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<AiDraftCaller>.Failure(new Error("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir."));
        if (_clinicContext.ClinicId is not { } clinicId)
            return Result<AiDraftCaller>.Failure(AiErrors.ClinicSelectionRequired);
        if (_clientContext.UserId is not { } userId)
            return Result<AiDraftCaller>.Failure(new Error("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok."));

        var scope = await _scopeResolver.ResolveAsync(tenantId, clinicId, ct);
        return scope.IsSuccess
            ? Result<AiDraftCaller>.Success(new AiDraftCaller(tenantId, clinicId, userId))
            : Result<AiDraftCaller>.Failure(scope.Error);
    }

    /// <summary>Özelliğin uygulayıcısı ve kullanıcının izni.</summary>
    public Result<IAiDraftApplier> AuthorizeFeature(AiFeature feature)
    {
        if (_appliers.For(feature) is not { } applier)
            return Result<IAiDraftApplier>.Failure(AiErrors.DraftFeatureNotSupported);
        return _permissions.HasPermission(applier.RequiredPermission)
            ? Result<IAiDraftApplier>.Success(applier)
            : Result<IAiDraftApplier>.Failure(AiErrors.DraftForbidden);
    }

    /// <summary>Taslağı (izlenen) yükler ve erişimi denetler.</summary>
    public async Task<Result<AiDraftAccessGrant>> LoadAsync(Guid draftId, CancellationToken ct)
    {
        var caller = await ResolveCallerAsync(ct);
        if (!caller.IsSuccess)
            return Result<AiDraftAccessGrant>.Failure(caller.Error);

        var draft = await _drafts.FirstOrDefaultAsync(new AiDraftByIdSpec(caller.Value!.TenantId, draftId), ct);
        if (draft is null || draft.ClinicId != caller.Value.ClinicId)
            return Result<AiDraftAccessGrant>.Failure(AiErrors.DraftNotFound);

        var applier = AuthorizeFeature(draft.Feature);
        return applier.IsSuccess
            ? Result<AiDraftAccessGrant>.Success(new AiDraftAccessGrant(caller.Value, draft, applier.Value!))
            : Result<AiDraftAccessGrant>.Failure(applier.Error);
    }
}

public sealed record AiDraftCaller(Guid TenantId, Guid ClinicId, Guid UserId);

public sealed record AiDraftAccessGrant(AiDraftCaller Caller, AiDraft Draft, IAiDraftApplier Applier);

// ----- Getir -----

public sealed record GetAiDraftQuery(Guid DraftId) : IRequest<Result<AiDraftDto>>;

public sealed class GetAiDraftQueryHandler : IRequestHandler<GetAiDraftQuery, Result<AiDraftDto>>
{
    private readonly AiDraftAccess _access;
    private readonly TimeProvider _timeProvider;

    public GetAiDraftQueryHandler(AiDraftAccess access, TimeProvider timeProvider)
    {
        _access = access;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AiDraftDto>> Handle(GetAiDraftQuery request, CancellationToken ct)
    {
        var access = await _access.LoadAsync(request.DraftId, ct);
        return access.IsSuccess
            ? Result<AiDraftDto>.Success(AiDraftDto.From(access.Value!.Draft, _timeProvider.GetUtcNow().UtcDateTime))
            : Result<AiDraftDto>.Failure(access.Error);
    }
}

// ----- Bir kaydın bekleyen taslakları -----

/// <summary>Bir kayıt için karar bekleyen taslaklar (ekran yeniden açılınca taslağa dönmek için); yalnız izinli özellikler.</summary>
public sealed record ListPendingAiDraftsQuery(string TargetType, Guid TargetId) : IRequest<Result<IReadOnlyList<AiDraftDto>>>;

public sealed class ListPendingAiDraftsQueryValidator : AbstractValidator<ListPendingAiDraftsQuery>
{
    public ListPendingAiDraftsQueryValidator()
    {
        RuleFor(x => x.TargetType).NotEmpty().MaximumLength(AiDraft.TargetTypeMaxLength);
        RuleFor(x => x.TargetId).NotEmpty();
    }
}

public sealed class ListPendingAiDraftsQueryHandler
    : IRequestHandler<ListPendingAiDraftsQuery, Result<IReadOnlyList<AiDraftDto>>>
{
    private readonly AiDraftAccess _access;
    private readonly IReadRepository<AiDraft> _drafts;
    private readonly TimeProvider _timeProvider;

    public ListPendingAiDraftsQueryHandler(AiDraftAccess access, IReadRepository<AiDraft> drafts, TimeProvider timeProvider)
    {
        _access = access;
        _drafts = drafts;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<IReadOnlyList<AiDraftDto>>> Handle(ListPendingAiDraftsQuery request, CancellationToken ct)
    {
        var caller = await _access.ResolveCallerAsync(ct);
        if (!caller.IsSuccess)
            return Result<IReadOnlyList<AiDraftDto>>.Failure(caller.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var drafts = await _drafts.ListAsync(
            new PendingAiDraftsForTargetSpec(caller.Value!.TenantId, caller.Value.ClinicId, request.TargetType.Trim(), request.TargetId, utcNow),
            ct);

        IReadOnlyList<AiDraftDto> visible = drafts
            .Where(d => _access.AuthorizeFeature(d.Feature).IsSuccess)
            .Select(d => AiDraftDto.From(d, utcNow))
            .ToList();
        return Result<IReadOnlyList<AiDraftDto>>.Success(visible);
    }
}

// ----- Kabul -----

/// <summary>
/// Taslağı kabul eder: içerik (düzenlenmişse <see cref="EditedContent"/>) özelliğin uygulayıcısıyla hedef kayda yazılır, taslak
/// <c>Accepted</c> / <c>AcceptedWithEdits</c> olur ve içeriği silinir. Hepsi tek transaction; uygulayıcı başarısızsa hiçbir şey
/// yazılmaz. Düzenlenmiş içerik klinik veridir, denetim yüküne yazılmaz (<c>JsonIgnore</c>; AI-010).
/// </summary>
public sealed record AcceptAiDraftCommand(Guid DraftId, [property: JsonIgnore] JsonObject? EditedContent)
    : IRequest<Result<AiDraftDto>>, IAuditableRequest, ITransactionalRollbackOnFailureRequest
{
    public string AuditAction => "Ai.Draft.Accept";
    public string? AuditTarget => $"AiDraftId={DraftId}";
}

public sealed class AcceptAiDraftCommandValidator : AbstractValidator<AcceptAiDraftCommand>
{
    public AcceptAiDraftCommandValidator()
    {
        RuleFor(x => x.DraftId).NotEmpty();
        RuleFor(x => x.EditedContent)
            .Must(c => c is null || c.ToJsonString().Length <= AiDraft.ContentMaxLength)
            .WithMessage($"Düzenlenmiş içerik en çok {AiDraft.ContentMaxLength} karakter olabilir.");
    }
}

public sealed class AcceptAiDraftCommandHandler : IRequestHandler<AcceptAiDraftCommand, Result<AiDraftDto>>
{
    private readonly AiDraftAccess _access;
    private readonly IRepository<AiDraft> _drafts;
    private readonly ITransactionLock _locks;
    private readonly TimeProvider _timeProvider;

    public AcceptAiDraftCommandHandler(
        AiDraftAccess access,
        IRepository<AiDraft> drafts,
        ITransactionLock locks,
        TimeProvider timeProvider)
    {
        _access = access;
        _drafts = drafts;
        _locks = locks;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AiDraftDto>> Handle(AcceptAiDraftCommand request, CancellationToken ct)
    {
        // Aynı taslağın iki kez uygulanmasını önler (iki sekme / çift tık): ikinci istek ilki bitince durumu görür.
        await _locks.AcquireAsync(LockName(request.DraftId), ct);

        var access = await _access.LoadAsync(request.DraftId, ct);
        if (!access.IsSuccess)
            return Fail(access.Error);
        var (caller, draft, applier) = access.Value!;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var decidable = draft.EnsureDecidable(utcNow);
        if (!decidable.IsSuccess)
            return Fail(decidable.Error);

        var original = JsonNode.Parse(draft.Content!) as JsonObject;
        if (original is null)
            return Fail(AiErrors.InvalidDraftContent("Taslak içeriği okunamadı."));

        var wasEdited = request.EditedContent is not null && !JsonNode.DeepEquals(request.EditedContent, original);
        var content = wasEdited ? request.EditedContent! : original;

        var applied = await applier.ApplyAsync(
            new AiDraftApplication(draft.Id, caller.TenantId, caller.ClinicId, draft.TargetType, draft.TargetId, content, wasEdited),
            ct);
        if (!applied.IsSuccess)
            return Fail(applied.Error);

        var accepted = draft.Accept(caller.UserId, wasEdited, applied.Value, utcNow);
        if (!accepted.IsSuccess)
            return Fail(accepted.Error);

        await _drafts.SaveChangesAsync(ct);
        return Result<AiDraftDto>.Success(AiDraftDto.From(draft, utcNow));
    }

    internal static string LockName(Guid draftId) => $"ai-drafts:{draftId:N}";

    private static Result<AiDraftDto> Fail(Error error) => Result<AiDraftDto>.Failure(error);
}

// ----- At -----

/// <summary>Taslağı atar: hiçbir kayda yazılmaz, içerik silinir; karar denetim için kalır.</summary>
public sealed record DiscardAiDraftCommand(Guid DraftId)
    : IRequest<Result<AiDraftDto>>, IAuditableRequest, ITransactionalRollbackOnFailureRequest
{
    public string AuditAction => "Ai.Draft.Discard";
    public string? AuditTarget => $"AiDraftId={DraftId}";
}

public sealed class DiscardAiDraftCommandValidator : AbstractValidator<DiscardAiDraftCommand>
{
    public DiscardAiDraftCommandValidator() => RuleFor(x => x.DraftId).NotEmpty();
}

public sealed class DiscardAiDraftCommandHandler : IRequestHandler<DiscardAiDraftCommand, Result<AiDraftDto>>
{
    private readonly AiDraftAccess _access;
    private readonly IRepository<AiDraft> _drafts;
    private readonly ITransactionLock _locks;
    private readonly TimeProvider _timeProvider;

    public DiscardAiDraftCommandHandler(
        AiDraftAccess access,
        IRepository<AiDraft> drafts,
        ITransactionLock locks,
        TimeProvider timeProvider)
    {
        _access = access;
        _drafts = drafts;
        _locks = locks;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AiDraftDto>> Handle(DiscardAiDraftCommand request, CancellationToken ct)
    {
        await _locks.AcquireAsync(AcceptAiDraftCommandHandler.LockName(request.DraftId), ct);

        var access = await _access.LoadAsync(request.DraftId, ct);
        if (!access.IsSuccess)
            return Result<AiDraftDto>.Failure(access.Error);

        var draft = access.Value!.Draft;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var discarded = draft.Discard(access.Value.Caller.UserId, utcNow);
        if (!discarded.IsSuccess)
            return Result<AiDraftDto>.Failure(discarded.Error);

        await _drafts.SaveChangesAsync(ct);
        return Result<AiDraftDto>.Success(AiDraftDto.From(draft, utcNow));
    }
}
