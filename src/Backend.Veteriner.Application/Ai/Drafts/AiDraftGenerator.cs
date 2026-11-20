using System.Text.Json.Nodes;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Ai.Drafts;

/// <summary>
/// Taslak üreten özelliklerin (AI-001/003/004) ortak üretim sırası (docs/AI_DRAFTS_API_CONTRACT.md §4): istek bağlamı → özelliğin
/// uygulayıcısı ve izni (yoksa model çağrılmaz) → özelliğe özgü hazırlık (hedef kayıt, girdi) → kullanım sınırları → sağlayıcı
/// çağrısı ve taslak. Hiçbir klinik kayıt yazılmaz; taslak ve kullanım kaydı yazılır.
/// </summary>
public sealed class AiDraftGenerator
{
    private readonly AiDraftAccess _access;
    private readonly AiUsageGuard _guard;
    private readonly AiInvoker _invoker;
    private readonly TimeProvider _timeProvider;

    public AiDraftGenerator(AiDraftAccess access, AiUsageGuard guard, AiInvoker invoker, TimeProvider timeProvider)
    {
        _access = access;
        _guard = guard;
        _invoker = invoker;
        _timeProvider = timeProvider;
    }

    /// <param name="prepare">Hedef kaydı yükler ve model girdisini kurar; başarısızsa model çağrılmaz.</param>
    public async Task<Result<AiDraftDto>> GenerateAsync(
        AiFeature feature,
        Func<AiDraftCaller, CancellationToken, Task<Result<AiDraftRequest>>> prepare,
        CancellationToken ct)
    {
        var caller = await _access.ResolveCallerAsync(ct);
        if (!caller.IsSuccess)
            return Fail(caller.Error);
        var (tenantId, clinicId, userId) = caller.Value!;

        var applier = _access.AuthorizeFeature(feature);
        if (!applier.IsSuccess)
            return Fail(applier.Error);

        var prepared = await prepare(caller.Value, ct);
        if (!prepared.IsSuccess)
            return Fail(prepared.Error);
        var request = prepared.Value!;

        var grant = await _guard.CheckAsync(tenantId, clinicId, userId, ct);
        if (!grant.IsSuccess)
            return Fail(grant.Error);

        var draft = await _invoker.InvokeForDraftAsync(
            new AiInvocation(
                tenantId, clinicId, userId, feature, request.SubjectId, request.Sources, grant.Value!,
                request.Prompt.Version, request.Prompt.System, request.UserContent, request.Prompt.Schema()),
            request.Target,
            request.Parse,
            ct);
        return draft.IsSuccess
            ? Result<AiDraftDto>.Success(AiDraftDto.From(draft.Value!, _timeProvider.GetUtcNow().UtcDateTime))
            : Fail(draft.Error);
    }

    private static Result<AiDraftDto> Fail(Error error) => Result<AiDraftDto>.Failure(error);
}

/// <summary>Sürümlü istem: sistem metni ve yanıt şeması birlikte değişir, <see cref="Version"/> artırılır (AI-010 denetiminde).</summary>
public sealed record AiDraftPrompt(string Version, string System, Func<JsonObject> Schema);

/// <param name="SubjectId">Denetimdeki konu (hayvan).</param>
/// <param name="Parse">Model yanıtını doğrulayıp taslak içeriğine çevirir; <c>null</c> → <c>Ai.InvalidOutput</c>.</param>
public sealed record AiDraftRequest(
    AiDraftTarget Target,
    Guid SubjectId,
    IReadOnlyCollection<AiSourceRef> Sources,
    AiDraftPrompt Prompt,
    string UserContent,
    Func<string?, JsonObject?> Parse);
