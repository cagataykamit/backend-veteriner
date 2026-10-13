using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.ExportOutstandingBalancesReport;

public sealed record ExportOutstandingBalancesReportQuery(
    Guid? ClinicId,
    decimal? MinAmount,
    string? Search) : IRequest<Result<OutstandingBalancesCsvExportResult>>;

public sealed record ExportOutstandingBalancesReportXlsxQuery(
    Guid? ClinicId,
    decimal? MinAmount,
    string? Search) : IRequest<Result<OutstandingBalancesXlsxExportResult>>;

/// <summary>CSV bytes (UTF-8 BOM) + dosya adı.</summary>
public sealed record OutstandingBalancesCsvExportResult(byte[] ContentUtf8Bom, string FileDownloadName);

/// <summary>XLSX bytes + indirme dosya adı.</summary>
public sealed record OutstandingBalancesXlsxExportResult(byte[] Content, string FileDownloadName);
