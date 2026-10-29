using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;

public sealed record ExportPaymentsReportPdfQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    PaymentMethod? Method,
    Guid? ClientId,
    Guid? PetId,
    string? Search) : IRequest<Result<RegisterExportResult>>;
