using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;

public sealed record ExportPaymentsReportXlsxQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    PaymentMethod? Method,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<PaymentsXlsxExportResult>>;

/// <summary>XLSX bytes + indirme dosya adı.</summary>
public sealed record PaymentsXlsxExportResult(byte[] Content, string FileDownloadName);
