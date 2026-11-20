using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai.PetHistorySummary;

/// <summary>
/// AI hasta geçmişi özeti taslağı (AI-002; docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §2). Hiçbir klinik kayıt yazmaz ve taslak
/// kaydı oluşturmaz; yalnız kullanım/denetim kaydı yazar. Aktif klinik bağlamı gerekir (kullanım ve sınır kliniğe yazılır);
/// kayıtlar o klinikle sınırlıdır.
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
    private readonly AiInvoker _invoker;
    private readonly TimeProvider _timeProvider;

    public GeneratePetHistorySummaryCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        AiUsageGuard guard,
        PetHistoryAiContextBuilder contextBuilder,
        AiInvoker invoker,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _guard = guard;
        _contextBuilder = contextBuilder;
        _invoker = invoker;
        _timeProvider = timeProvider;
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

        var parsed = await _invoker.InvokeAsync(
            new AiInvocation(
                tenantId,
                clinicId,
                userId,
                AiFeature.PetHistorySummary,
                request.PetId,
                context.Sources.Values.Select(s => new AiSourceRef(s.Type, s.Id)).ToList(),
                grant.Value,
                PetHistoryAiPrompt.Version,
                PetHistoryAiPrompt.System,
                context.UserContent,
                PetHistoryAiPrompt.Schema()),
            text => PetHistoryAiOutputParser.Parse(text, context.Sources),
            ct);
        if (!parsed.IsSuccess)
            return Fail(parsed.Error);

        return Result<PetHistoryAiSummaryDto>.Success(new PetHistoryAiSummaryDto(
            request.PetId, PetHistoryAiSummaryStatus.Generated, true, utcNow, grant.Value.Model,
            context.Coverage, parsed.Value!.Sections, parsed.Value.DroppedItemCount));
    }

    private static Result<PetHistoryAiSummaryDto> Fail(Error error) => Result<PetHistoryAiSummaryDto>.Failure(error);
}
