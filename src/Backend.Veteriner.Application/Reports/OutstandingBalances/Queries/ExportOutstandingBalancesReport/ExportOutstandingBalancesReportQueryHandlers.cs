using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Pdf;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.ExportOutstandingBalancesReport;

public sealed class ExportOutstandingBalancesReportQueryHandler
    : IRequestHandler<ExportOutstandingBalancesReportQuery, Result<OutstandingBalancesCsvExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IOutstandingBalancesReportReader _reader;
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public ExportOutstandingBalancesReportQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IOutstandingBalancesReportReader reader,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<OutstandingBalancesCsvExportResult>> Handle(
        ExportOutstandingBalancesReportQuery request,
        CancellationToken ct)
    {
        var loaded = await OutstandingBalancesReportExportPipeline.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _reader,
            request.ClinicId, request.MinAmount, request.Search, _trialLimitPolicy, RegisterExportFormat.Csv, ct);
        if (!loaded.IsSuccess)
            return Result<OutstandingBalancesCsvExportResult>.Failure(loaded.Error);

        var bytes = OutstandingBalancesCsvWriter.WriteUtf8Bom(loaded.Value!);
        var fileName = $"borc-raporu-{OutstandingBalancesReportExportPipeline.FileDateStamp(_timeProvider.GetUtcNow().UtcDateTime)}.csv";

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
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public ExportOutstandingBalancesReportXlsxQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IOutstandingBalancesReportReader reader,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<OutstandingBalancesXlsxExportResult>> Handle(
        ExportOutstandingBalancesReportXlsxQuery request,
        CancellationToken ct)
    {
        var loaded = await OutstandingBalancesReportExportPipeline.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _reader,
            request.ClinicId, request.MinAmount, request.Search, _trialLimitPolicy, RegisterExportFormat.Xlsx, ct);
        if (!loaded.IsSuccess)
            return Result<OutstandingBalancesXlsxExportResult>.Failure(loaded.Error);

        var bytes = OutstandingBalancesXlsxWriter.WriteWorkbook(loaded.Value!);
        var fileName = $"borc-raporu-{OutstandingBalancesReportExportPipeline.FileDateStamp(_timeProvider.GetUtcNow().UtcDateTime)}.xlsx";

        return Result<OutstandingBalancesXlsxExportResult>.Success(new OutstandingBalancesXlsxExportResult(bytes, fileName));
    }
}

public sealed class ExportOutstandingBalancesReportPdfQueryHandler
    : IRequestHandler<ExportOutstandingBalancesReportPdfQuery, Result<RegisterExportResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IOutstandingBalancesReportReader _reader;
    private readonly ITrialLimitPolicy _trialLimitPolicy;
    private readonly TimeProvider _timeProvider;

    public ExportOutstandingBalancesReportPdfQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IOutstandingBalancesReportReader reader,
        ITrialLimitPolicy trialLimitPolicy,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
        _trialLimitPolicy = trialLimitPolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<RegisterExportResult>> Handle(
        ExportOutstandingBalancesReportPdfQuery request,
        CancellationToken ct)
    {
        var loaded = await OutstandingBalancesReportExportPipeline.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _reader,
            request.ClinicId, request.MinAmount, request.Search, _trialLimitPolicy, RegisterExportFormat.Pdf, ct);
        if (!loaded.IsSuccess)
            return Result<RegisterExportResult>.Failure(loaded.Error);

        // Anlık durum raporu: dönem yok, oluşturma zamanı altbilgide.
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var bytes = ReportPdfWriter.Write(
            new ReportPdfHeader(OutstandingBalancesPdfColumns.Title, Period: null, utcNow), OutstandingBalancesPdfColumns.All, loaded.Value!);
        var fileName = $"borc-raporu-{OutstandingBalancesReportExportPipeline.FileDateStamp(utcNow)}.pdf";

        return Result<RegisterExportResult>.Success(new RegisterExportResult(bytes, fileName));
    }
}
