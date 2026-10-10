using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Commands.Update;

/// <summary>
/// PUT /api/v1/payments/{id}. Tutar, müşteri, klinik veya para birimi mevcut değerden farklıysa bu bir düzeltmedir:
/// <c>Payments.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir; tahsisli ödemede reddedilir.
/// </summary>
public sealed record UpdatePaymentCommand(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    Guid? PetId,
    Guid? AppointmentId,
    Guid? ExaminationId,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime PaidAtUtc,
    string? Notes,
    string? Reason = null)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Payment.Update";
    public string? AuditTarget => $"PaymentId={Id}";
}
