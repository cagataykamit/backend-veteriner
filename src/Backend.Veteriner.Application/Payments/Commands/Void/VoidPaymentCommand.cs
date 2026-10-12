using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Commands.Void;

/// <summary>
/// POST /api/v1/payments/{id}/void. <c>Payments.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir.
/// Ödeme "hiç alınmamış" sayılır: tahsisleri geri alınır, ücretlerin açık tutarı artar, tahsilat toplamlarından düşer.
/// </summary>
public sealed record VoidPaymentCommand(Guid Id, string? Reason)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Payment.Void";
    public string? AuditTarget => $"PaymentId={Id}";
}
