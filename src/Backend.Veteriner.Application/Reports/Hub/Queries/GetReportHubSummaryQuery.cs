using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Hub.Queries;

/// <summary>
/// Rapor Merkezi ana ekran özeti (ADR-052). <c>Period</c>: <c>today</c> | <c>week</c> | <c>month</c>. Sayılar tek şubeye göredir:
/// <c>ClinicId</c> ya da aktif klinik bağlamı (REPORT_HUB_SUMMARY_API_CONTRACT.md).
/// </summary>
public sealed record GetReportHubSummaryQuery(string? Period, Guid? ClinicId) : IRequest<Result<ReportHubSummaryDto>>;

public sealed class GetReportHubSummaryQueryHandler : IRequestHandler<GetReportHubSummaryQuery, Result<ReportHubSummaryDto>>
{
    private const string Module = "ReportsHub";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly TimeProvider _timeProvider;
    private readonly ReportHubFinanceLoader _finance;
    private readonly ReportHubWorkLoader _work;

    public GetReportHubSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        TimeProvider timeProvider,
        ReportHubFinanceLoader finance,
        ReportHubWorkLoader work)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _timeProvider = timeProvider;
        _finance = finance;
        _work = work;
    }

    public async ValueTask<Result<ReportHubSummaryDto>> Handle(GetReportHubSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ReportHubSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (!ReportHubPeriod.IsKnown(request.Period))
        {
            return Result<ReportHubSummaryDto>.Failure(
                $"{Module}.PeriodInvalid",
                "period şunlardan biri olmalı: today, week, month.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<ReportHubSummaryDto>.Failure(ClinicContextRule.Mismatch(Module));

        if ((request.ClinicId ?? _clinicContext.ClinicId) is not { } requestedClinicId)
        {
            return Result<ReportHubSummaryDto>.Failure(
                $"{Module}.ClinicScopeRequired",
                "Rapor özeti için klinik belirlenemedi (clinicId veya aktif klinik bağlamı gerekir).");
        }

        var scope = await _scopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scope.IsSuccess)
            return Result<ReportHubSummaryDto>.Failure(scope.Error);
        var clinicId = scope.Value!.SingleClinicId!.Value;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var period = ReportHubPeriod.For(request.Period!, nowUtc);
        var today = period.Name == ReportHubPeriod.Today ? period : ReportHubPeriod.For(ReportHubPeriod.Today, nowUtc);

        var todayCash = await _finance.LoadTodayCashAsync(tenantId, clinicId, today, ct);
        if (!todayCash.IsSuccess)
            return Result<ReportHubSummaryDto>.Failure(todayCash.Error);

        var collections = await _finance.LoadCollectionsAsync(tenantId, clinicId, period, ct);
        if (!collections.IsSuccess)
            return Result<ReportHubSummaryDto>.Failure(collections.Error);

        var petIdsNotReceivingCare = await _work.LoadPetIdsNotReceivingCareAsync(tenantId, ct);

        return Result<ReportHubSummaryDto>.Success(new ReportHubSummaryDto(
            period.Name,
            clinicId,
            "TRY",
            nowUtc,
            new ReportHubWindowDto(period.Current.From, period.Current.To, period.Previous.From, period.Previous.To),
            todayCash.Value,
            await _finance.LoadTurnoverAsync(tenantId, clinicId, period, ct),
            collections.Value,
            await _finance.LoadOutstandingAsync(tenantId, clinicId, ct),
            await _work.LoadAppointmentsAsync(tenantId, clinicId, period, ct),
            await _work.LoadExaminationsAsync(tenantId, clinicId, period, ct),
            await _work.LoadVaccinationsAsync(tenantId, clinicId, period, nowUtc, petIdsNotReceivingCare, ct),
            await _work.LoadAttentionAsync(tenantId, clinicId, nowUtc, petIdsNotReceivingCare, ct)));
    }
}
