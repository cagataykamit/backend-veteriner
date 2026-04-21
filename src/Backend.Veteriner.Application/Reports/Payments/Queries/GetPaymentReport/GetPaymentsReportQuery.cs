using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;

/// <summary>
/// <c>from</c>/<c>to</c>: UTC anları — filtre <see cref="Domain.Payments.Payment.PaidAtUtc"/> üzerinde
/// <c>[from, to]</c> kapalı aralık (her iki uç dahil).
/// </summary>
public sealed record GetPaymentsReportQuery(
    DateTime FromUtc,
    DateTime ToUtc,
    Guid? ClinicId,
    PaymentMethod? Method,
    Guid? ClientId,
    Guid? PetId,
    string? Search,
    int Page,
    int PageSize) : IRequest<Result<PaymentReportResultDto>>;
