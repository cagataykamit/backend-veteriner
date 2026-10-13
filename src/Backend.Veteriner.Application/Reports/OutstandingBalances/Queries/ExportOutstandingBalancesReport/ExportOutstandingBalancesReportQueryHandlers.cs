using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.ExportOutstandingBalancesReport;

public sealed class ExportOutstandingBalancesReportQueryHandler
    : IRequestHandler<ExportOutstandingBalancesReportQuery, Result<OutstandingBalancesCsvExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IOutstandingBalancesReportReader _reader;

    public ExportOutstandingBalancesReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IOutstandingBalancesReportReader reader)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
    }

    public async Task<Result<OutstandingBalancesCsvExportResult>> Handle(
        ExportOutstandingBalancesReportQuery request,
        CancellationToken ct)
    {
        var loaded = await OutstandingBalancesReportExportPipeline.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _reader,
            request.ClinicId, request.MinAmount, request.Search, ct);
        if (!loaded.IsSuccess)
            return Result<OutstandingBalancesCsvExportResult>.Failure(loaded.Error);

        var bytes = OutstandingBalancesCsvWriter.WriteUtf8Bom(loaded.Value!);
        var fileName = $"borc-raporu-{OutstandingBalancesReportExportPipeline.FileDateStamp(DateTime.UtcNow)}.csv";

        return Result<OutstandingBalancesCsvExportResult>.Success(new OutstandingBalancesCsvExportResult(bytes, fileName));
    }
}

public sealed class ExportOutstandingBalancesReportXlsxQueryHandler
    : IRequestHandler<ExportOutstandingBalancesReportXlsxQuery, Result<OutstandingBalancesXlsxExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IOutstandingBalancesReportReader _reader;

    public ExportOutstandingBalancesReportXlsxQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IOutstandingBalancesReportReader reader)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
    }

    public async Task<Result<OutstandingBalancesXlsxExportResult>> Handle(
        ExportOutstandingBalancesReportXlsxQuery request,
        CancellationToken ct)
    {
        var loaded = await OutstandingBalancesReportExportPipeline.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _reader,
            request.ClinicId, request.MinAmount, request.Search, ct);
        if (!loaded.IsSuccess)
            return Result<OutstandingBalancesXlsxExportResult>.Failure(loaded.Error);

        var bytes = OutstandingBalancesXlsxWriter.WriteWorkbook(loaded.Value!);
        var fileName = $"borc-raporu-{OutstandingBalancesReportExportPipeline.FileDateStamp(DateTime.UtcNow)}.xlsx";

        return Result<OutstandingBalancesXlsxExportResult>.Success(new OutstandingBalancesXlsxExportResult(bytes, fileName));
    }
}
