using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;

public sealed record ExportPaymentsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    PaymentMethod? Method,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<PaymentsCsvExportResult>>;

/// <summary>CSV bytes (UTF-8 BOM) + dosya adı.</summary>
public sealed record PaymentsCsvExportResult(byte[] ContentUtf8Bom, string FileDownloadName);
