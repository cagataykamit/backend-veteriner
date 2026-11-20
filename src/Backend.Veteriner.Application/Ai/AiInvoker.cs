using System.Diagnostics;
using System.Text.Json.Nodes;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// AI sağlayıcı çağrısının tek yeri (<see cref="AiUsageGuard.CheckAsync"/> sonrasında): çağrı, sonucun sınıflanması, kredi
/// harcaması ve kota uyarısı (AI-007, <see cref="AiCreditAccount"/>), kullanım ve denetim kaydı (AI-008, AI-010), yalnız ölçü
/// içeren günlük ve taslak üreten özelliklerde taslak (AI-009). Taslak, kullanım kaydı ve uyarı birlikte kaydedilir; başarısız üretimde taslak oluşmaz ama kullanım kaydı yazılır. İstem ve yanıt metni
/// günlüğe ve denetime yazılmaz (ADR-008 madde 10).
/// </summary>
public sealed class AiInvoker
{
    private readonly IAiTextGenerator _generator;
    private readonly IRepository<AiUsageRecord> _usageRecords;
    private readonly IRepository<AiDraft> _drafts;
    private readonly IEnumerable<IAiDraftApplier> _appliers;
    private readonly AiCreditAccount _credits;
    private readonly AiOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AiInvoker> _logger;

    public AiInvoker(
        IAiTextGenerator generator,
        IRepository<AiUsageRecord> usageRecords,
        IRepository<AiDraft> drafts,
        IEnumerable<IAiDraftApplier> appliers,
        AiCreditAccount credits,
        IOptions<AiOptions> options,
        TimeProvider timeProvider,
        ILogger<AiInvoker> logger)
    {
        _generator = generator;
        _usageRecords = usageRecords;
        _drafts = drafts;
        _appliers = appliers;
        _credits = credits;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Kaydedilmeyen çıktı (ör. hasta geçmişi özeti): <paramref name="parse"/> <c>null</c> dönerse geçersiz çıktıdır.</summary>
    public async Task<Result<T>> InvokeAsync<T>(AiInvocation invocation, Func<string?, T?> parse, CancellationToken ct)
        where T : class
        => (await RunAsync(invocation, parse, toDraft: null, ct)).Result;

    /// <summary>
    /// Taslak üreten özellik (AI-001/003/004): <paramref name="parse"/> yanıtı doğrulayıp yapılandırılmış içeriğe çevirir;
    /// başarılıysa <see cref="AiDraft"/> oluşur (hiçbir klinik kayda yazılmaz). Özelliğin <see cref="IAiDraftApplier"/>'ı
    /// yoksa model çağrılmaz.
    /// </summary>
    public async Task<Result<AiDraft>> InvokeForDraftAsync(
        AiInvocation invocation,
        AiDraftTarget target,
        Func<string?, JsonObject?> parse,
        CancellationToken ct)
    {
        if (_appliers.For(invocation.Feature) is null)
            return Result<AiDraft>.Failure(AiErrors.DraftFeatureNotSupported);

        var run = await RunAsync(
            invocation,
            text => parse(text)?.ToJsonString() is { Length: > 0 and <= AiDraft.ContentMaxLength } json ? json : null,
            json => AiDraft.Create(
                invocation.TenantId,
                invocation.ClinicId,
                invocation.Feature,
                target.Type,
                target.Id,
                invocation.Sources,
                json,
                invocation.Grant.Model,
                invocation.PromptVersion,
                invocation.UserId,
                _timeProvider.GetUtcNow().UtcDateTime,
                _options.DraftLifetime),
            ct);

        return run.Result.IsSuccess
            ? Result<AiDraft>.Success(run.Draft!)
            : Result<AiDraft>.Failure(run.Result.Error);
    }

    private async Task<(Result<T> Result, AiDraft? Draft)> RunAsync<T>(
        AiInvocation invocation,
        Func<string?, T?> parse,
        Func<T, AiDraft>? toDraft,
        CancellationToken ct)
        where T : class
    {
        var grant = invocation.Grant;
        var stopwatch = Stopwatch.StartNew();
        var generation = await _generator.GenerateJsonAsync(
            new AiGenerationRequest(grant.Model, invocation.SystemPrompt, invocation.UserContent, invocation.Schema, grant.MaxOutputTokens),
            ct);
        stopwatch.Stop();

        var parsed = generation.Status == AiGenerationStatus.Completed ? parse(generation.Text) : null;
        var outcome = generation.Status switch
        {
            AiGenerationStatus.Completed => parsed is null ? AiUsageOutcome.InvalidOutput : AiUsageOutcome.Succeeded,
            AiGenerationStatus.Truncated => AiUsageOutcome.Truncated,
            AiGenerationStatus.Refused => AiUsageOutcome.Refused,
            _ => AiUsageOutcome.ProviderError,
        };

        var draft = outcome == AiUsageOutcome.Succeeded && toDraft is not null ? toDraft(parsed!) : null;
        if (draft is not null)
            await _drafts.AddAsync(draft, ct);

        // Gerçek token'dan kredi; başarısız ama token harcayan istek de harcar (maliyet oluştu).
        var cost = grant.Price.CostOf(generation.InputTokens, generation.OutputTokens);
        var charge = await _credits.ChargeAsync(
            invocation.TenantId, invocation.ClinicId, (long)generation.InputTokens + generation.OutputTokens, cost, ct);

        var record = AiUsageRecord.Create(
            invocation.TenantId, invocation.ClinicId, invocation.UserId, invocation.Feature, invocation.SubjectId,
            invocation.Sources, draft?.Id, grant.Model, invocation.PromptVersion,
            generation.InputTokens, generation.OutputTokens, cost, charge.Credits, charge.FundCredits,
            (int)stopwatch.ElapsedMilliseconds, outcome, _timeProvider.GetUtcNow().UtcDateTime);
        await _usageRecords.AddAsync(record, ct);
        await _usageRecords.SaveChangesAsync(ct);

        // Yalnız ölçü günlüğe yazılır; istem ve yanıt metni klinik veridir (ADR-008 madde 10).
        _logger.LogInformation(
            "AI {Feature} {Outcome}: model={Model} prompt={PromptVersion} input={InputTokens} output={OutputTokens} durationMs={DurationMs}",
            record.Feature, record.Outcome, record.Model, record.PromptVersion, record.InputTokens, record.OutputTokens, record.DurationMs);

        var result = outcome switch
        {
            AiUsageOutcome.Succeeded => Result<T>.Success(parsed!),
            AiUsageOutcome.Refused => Result<T>.Failure(AiErrors.Refused),
            AiUsageOutcome.ProviderError => Result<T>.Failure(generation.FailureKind == AiFailureKind.Rejected
                ? AiErrors.ProviderRejected
                : AiErrors.ProviderUnavailable),
            _ => Result<T>.Failure(AiErrors.InvalidOutput),
        };
        return (result, draft);
    }
}

/// <summary>Denetimden geçmiş tek AI isteği.</summary>
/// <param name="SubjectId">Özelliğin konusu olan kayıt (ör. hayvan); denetim içindir.</param>
/// <param name="Sources">Modele gönderilen kayıtlar (yalnız tür ve kimlik denetime yazılır).</param>
/// <param name="PromptVersion">İstem ve şema sürümü (ör. <c>pet-history-summary/1</c>); istem değişince artırılır.</param>
public sealed record AiInvocation(
    Guid TenantId,
    Guid ClinicId,
    Guid UserId,
    AiFeature Feature,
    Guid? SubjectId,
    IReadOnlyCollection<AiSourceRef> Sources,
    AiUsageGrant Grant,
    string PromptVersion,
    string SystemPrompt,
    string UserContent,
    JsonObject Schema);

/// <summary>Taslağın kabulde yazılacağı kayıt (ör. <c>Examination</c> + kimlik).</summary>
public sealed record AiDraftTarget(string Type, Guid Id);
