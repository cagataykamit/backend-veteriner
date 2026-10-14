using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.DailyTransactions.Queries;

/// <summary>Tahsilat/kasa özeti (tarih aralığı). <c>From</c>/<c>To</c>: İstanbul takvim günleri, ikisi dahil (en çok 366 gün).</summary>
public sealed record GetCashSummaryReportQuery(DateOnly From, DateOnly To, Guid? ClinicId)
    : IRequest<Result<CashSummaryReportDto>>;

public sealed record ExportCashSummaryReportQuery(DateOnly From, DateOnly To, Guid? ClinicId, RegisterExportFormat Format)
    : IRequest<Result<RegisterExportResult>>;

/// <summary>Özet ve dışa aktarma aynı yüklemeden geçer (ekran = CSV = XLSX).</summary>
internal sealed class CashSummaryLoader
{
    /// <summary>Tek kasa özeti için işlenebilir en çok tahsilat/iptal olayı.</summary>
    public const int MaxEvents = RegisterReportConstants.MaxExportRows;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICashEventsReader _reader;

    public CashSummaryLoader(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader reader)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _reader = reader;
    }

    public async Task<Result<CashSummaryReportDto>> LoadAsync(DateOnly from, DateOnly to, Guid? clinicId, CancellationToken ct)
    {
        var filter = await DailyTransactionsReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _scopeResolver, from, to, clinicId, null, null, null, ct);
        if (!filter.IsSuccess)
            return Result<CashSummaryReportDto>.Failure(filter.Error);

        var rows = await _reader.ReadAsync(filter.Value!, MaxEvents, ct);
        if (rows.Count > MaxEvents)
        {
            return Result<CashSummaryReportDto>.Failure(
                "CashSummary.TooManyRows",
                $"Kasa özeti en fazla {MaxEvents} hareket işler; tarih aralığını daraltın.");
        }

        return Result<CashSummaryReportDto>.Success(CashSummaryBuilder.Build(from, to, rows));
    }
}

public sealed class GetCashSummaryReportQueryHandler : IRequestHandler<GetCashSummaryReportQuery, Result<CashSummaryReportDto>>
{
    private readonly CashSummaryLoader _loader;

    public GetCashSummaryReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader reader)
        => _loader = new CashSummaryLoader(tenantContext, clinicContext, scopeResolver, reader);

    public Task<Result<CashSummaryReportDto>> Handle(GetCashSummaryReportQuery request, CancellationToken ct)
        => _loader.LoadAsync(request.From, request.To, request.ClinicId, ct);
}

public sealed class ExportCashSummaryReportQueryHandler : IRequestHandler<ExportCashSummaryReportQuery, Result<RegisterExportResult>>
{
    private readonly CashSummaryLoader _loader;

    public ExportCashSummaryReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader reader)
        => _loader = new CashSummaryLoader(tenantContext, clinicContext, scopeResolver, reader);

    public async Task<Result<RegisterExportResult>> Handle(ExportCashSummaryReportQuery request, CancellationToken ct)
    {
        var report = await _loader.LoadAsync(request.From, request.To, request.ClinicId, ct);
        if (!report.IsSuccess)
            return Result<RegisterExportResult>.Failure(report.Error);

        return Result<RegisterExportResult>.Success(RegisterReportRunner.BuildFile(
            CashSummaryBuilder.ExportSpec, CashSummaryBuilder.ToExportRows(report.Value!), request.Format));
    }
}
