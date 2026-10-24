using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.DailyTransactions.Queries;

/// <summary>Günlük işlem dökümü (sayfalı). <c>From</c>/<c>To</c>: İstanbul takvim günleri, ikisi dahil (en çok 366 gün).</summary>
public sealed record GetDailyTransactionsReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    DailyTransactionKind? Kind,
    PaymentMethod? PaymentMethod,
    Guid? PerformedByUserId,
    int Page,
    int PageSize) : IRequest<Result<DailyTransactionsReportResultDto>>;

public sealed record ExportDailyTransactionsReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    DailyTransactionKind? Kind,
    PaymentMethod? PaymentMethod,
    Guid? PerformedByUserId,
    RegisterExportFormat Format) : IRequest<Result<RegisterExportResult>>;

public sealed class GetDailyTransactionsReportQueryHandler
    : IRequestHandler<GetDailyTransactionsReportQuery, Result<DailyTransactionsReportResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IDailyTransactionsReader _reader;

    public GetDailyTransactionsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IDailyTransactionsReader reader)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _reader = reader;
    }

    public async Task<Result<DailyTransactionsReportResultDto>> Handle(
        GetDailyTransactionsReportQuery request,
        CancellationToken ct)
    {
        var filter = await DailyTransactionsReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _scopeResolver,
            request.From, request.To, request.ClinicId, request.Kind, request.PaymentMethod, request.PerformedByUserId, ct);
        if (!filter.IsSuccess)
            return Result<DailyTransactionsReportResultDto>.Failure(filter.Error);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, RegisterReportConstants.MaxPageSize);
        var read = await _reader.ReadAsync(filter.Value!, (page - 1) * pageSize, pageSize, ct);

        return Result<DailyTransactionsReportResultDto>.Success(new DailyTransactionsReportResultDto(
            Charge.DefaultCurrency, request.From, request.To, read.Totals, read.TotalCount, read.Items));
    }
}

public sealed class ExportDailyTransactionsReportQueryHandler
    : IRequestHandler<ExportDailyTransactionsReportQuery, Result<RegisterExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IDailyTransactionsReader _reader;
    private readonly ITrialLimitPolicy _trialLimitPolicy;

    public ExportDailyTransactionsReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IDailyTransactionsReader reader,
        ITrialLimitPolicy trialLimitPolicy)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _reader = reader;
        _trialLimitPolicy = trialLimitPolicy;
    }

    public async Task<Result<RegisterExportResult>> Handle(ExportDailyTransactionsReportQuery request, CancellationToken ct)
    {
        var filter = await DailyTransactionsReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _scopeResolver,
            request.From, request.To, request.ClinicId, request.Kind, request.PaymentMethod, request.PerformedByUserId, ct);
        if (!filter.IsSuccess)
            return Result<RegisterExportResult>.Failure(filter.Error);

        // Satır tavanı kiracıya göre (TRIAL-004): deneme 1.000, ücretli ReportsSharedLimits.MaxExportRows.
        var rowLimit = await _trialLimitPolicy.GetExportRowLimitAsync(filter.Value!.TenantId, ct);
        var read = await _reader.ReadAsync(filter.Value, 0, rowLimit.MaxRows, ct);
        var withinLimit = rowLimit.Ensure(read.TotalCount, "Registers.ExportTooManyRows");
        if (!withinLimit.IsSuccess)
            return Result<RegisterExportResult>.Failure(withinLimit.Error);

        return Result<RegisterExportResult>.Success(
            RegisterReportRunner.BuildFile(DailyTransactionsReportColumns.Export, read.Items, request.Format));
    }
}
