using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Ai;

/// <param name="UnavailableCode">Kullanılamıyorsa nedenin hata kodu (<c>Ai.NotConfigured</c>, <c>Ai.ClinicNotEnabled</c>,
/// <c>Ai.ClinicQuotaExceeded</c>, <c>Ai.SystemBudgetExhausted</c>, <c>Clinics.ClinicSelectionRequired</c>); kullanılabiliyorsa <c>null</c>.</param>
/// <param name="ClinicMonthUsedPercent">Kliniğin bu ayki AI kullanımının sınıra oranı (0–100); dolar tutarı gösterilmez.</param>
public sealed record AiStatusDto(
    bool IsAvailable,
    string? UnavailableCode,
    bool IsConfigured,
    bool IsClinicEnabled,
    int ClinicMonthUsedPercent);

/// <summary>Arayüz AI düğmesini göstermek/kapatmak için sorar (docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §1). Kullanıcı hız sınırını içermez.</summary>
public sealed record GetAiStatusQuery : IRequest<Result<AiStatusDto>>;

public sealed class GetAiStatusQueryHandler : IRequestHandler<GetAiStatusQuery, Result<AiStatusDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly AiUsageGuard _guard;

    public GetAiStatusQueryHandler(ITenantContext tenantContext, IClinicContext clinicContext, AiUsageGuard guard)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _guard = guard;
    }

    public async ValueTask<Result<AiStatusDto>> Handle(GetAiStatusQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<AiStatusDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (_clinicContext.ClinicId is not { } clinicId)
            return Result<AiStatusDto>.Success(new AiStatusDto(false, AiErrors.ClinicSelectionRequired.Code, _guard.IsConfigured, false, 0));

        var availability = await _guard.GetAvailabilityAsync(tenantId, clinicId, ct);
        return Result<AiStatusDto>.Success(new AiStatusDto(
            availability.Blocker is null,
            availability.Blocker?.Code,
            availability.IsConfigured,
            availability.IsClinicEnabled,
            availability.ClinicMonthUsedPercent));
    }
}
