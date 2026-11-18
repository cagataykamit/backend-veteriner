using System.Diagnostics;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Ai.PetHistorySummary;

/// <summary>
/// AI hasta geçmişi özeti taslağı (AI-002; docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §2). Hiçbir klinik kayıt yazmaz; yalnız
/// kullanım kaydı yazar. Aktif klinik bağlamı gerekir (kullanım ve sınır kliniğe yazılır); kayıtlar o klinikle sınırlıdır.
/// </summary>
public sealed record GeneratePetHistorySummaryCommand(Guid PetId)
    : IRequest<Result<PetHistoryAiSummaryDto>>, IAuditableRequest
{
    public string AuditAction => "Ai.PetHistorySummary.Generate";
    public string? AuditTarget => $"PetId={PetId}";
}

public sealed class GeneratePetHistorySummaryCommandValidator : AbstractValidator<GeneratePetHistorySummaryCommand>
{
    public GeneratePetHistorySummaryCommandValidator() => RuleFor(x => x.PetId).NotEmpty();
}

public sealed class GeneratePetHistorySummaryCommandHandler
    : IRequestHandler<GeneratePetHistorySummaryCommand, Result<PetHistoryAiSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly AiUsageGuard _guard;
    private readonly PetHistoryAiContextBuilder _contextBuilder;
    private readonly IAiTextGenerator _generator;
    private readonly IRepository<AiUsageRecord> _usageRecords;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GeneratePetHistorySummaryCommandHandler> _logger;

    public GeneratePetHistorySummaryCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        AiUsageGuard guard,
        PetHistoryAiContextBuilder contextBuilder,
        IAiTextGenerator generator,
        IRepository<AiUsageRecord> usageRecords,
        TimeProvider timeProvider,
        ILogger<GeneratePetHistorySummaryCommandHandler> logger)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _guard = guard;
        _contextBuilder = contextBuilder;
        _generator = generator;
        _usageRecords = usageRecords;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<Result<PetHistoryAiSummaryDto>> Handle(GeneratePetHistorySummaryCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail(new Error("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir."));
        if (_clinicContext.ClinicId is not { } clinicId)
            return Fail(AiErrors.ClinicSelectionRequired);
        if (_clientContext.UserId is not { } userId)
            return Fail(new Error("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok."));

        var scope = await _scopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Fail(scope.Error);

        var grant = await _guard.CheckAsync(tenantId, clinicId, userId, ct);
        if (!grant.IsSuccess)
            return Fail(grant.Error);

        var context = await _contextBuilder.BuildAsync(tenantId, scope.Value!, request.PetId, grant.Value!.MaxInputTokens, ct);
        if (context is null)
            return Fail(new Error("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil."));

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (!context.HasClinicalRecords)
        {
            return Result<PetHistoryAiSummaryDto>.Success(new PetHistoryAiSummaryDto(
                request.PetId, PetHistoryAiSummaryStatus.NoRecords, true, utcNow, null, context.Coverage, [], 0));
        }

        var stopwatch = Stopwatch.StartNew();
        var generation = await _generator.GenerateJsonAsync(
            new AiGenerationRequest(
                grant.Value.Model,
                PetHistoryAiPrompt.System,
                context.UserContent,
                PetHistoryAiPrompt.Schema(),
                grant.Value.MaxOutputTokens),
            ct);
        stopwatch.Stop();

        var parsed = generation.Status == AiGenerationStatus.Completed
            ? PetHistoryAiOutputParser.Parse(generation.Text, context.Sources)
            : null;
        var outcome = generation.Status switch
        {
            AiGenerationStatus.Completed => parsed is null ? AiUsageOutcome.InvalidOutput : AiUsageOutcome.Succeeded,
            AiGenerationStatus.Truncated => AiUsageOutcome.Truncated,
            AiGenerationStatus.Refused => AiUsageOutcome.Refused,
            _ => AiUsageOutcome.ProviderError,
        };

        var record = AiUsageRecord.Create(
            tenantId, clinicId, userId, AiFeature.PetHistorySummary, request.PetId, grant.Value.Model,
            generation.InputTokens, generation.OutputTokens,
            grant.Value.Price.CostOf(generation.InputTokens, generation.OutputTokens),
            (int)stopwatch.ElapsedMilliseconds, outcome, _timeProvider.GetUtcNow().UtcDateTime);
        await _usageRecords.AddAsync(record, ct);
        await _usageRecords.SaveChangesAsync(ct);

        // Yalnız ölçü günlüğe yazılır; istem ve yanıt metni klinik veridir (ADR-008 madde 10).
        _logger.LogInformation(
            "AI {Feature} {Outcome}: model={Model} input={InputTokens} output={OutputTokens} durationMs={DurationMs}",
            record.Feature, record.Outcome, record.Model, record.InputTokens, record.OutputTokens, record.DurationMs);

        return outcome switch
        {
            AiUsageOutcome.Succeeded => Result<PetHistoryAiSummaryDto>.Success(new PetHistoryAiSummaryDto(
                request.PetId, PetHistoryAiSummaryStatus.Generated, true, utcNow, grant.Value.Model,
                context.Coverage, parsed!.Sections, parsed.DroppedItemCount)),
            AiUsageOutcome.Refused => Fail(AiErrors.Refused),
            AiUsageOutcome.ProviderError => Fail(generation.FailureKind == AiFailureKind.Rejected
                ? AiErrors.ProviderRejected
                : AiErrors.ProviderUnavailable),
            _ => Fail(AiErrors.InvalidOutput),
        };
    }

    private static Result<PetHistoryAiSummaryDto> Fail(Error error) => Result<PetHistoryAiSummaryDto>.Failure(error);
}
