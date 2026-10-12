using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Charges.Commands.Void;

/// <summary>
/// POST /api/v1/charges/{id}/void. <c>Charges.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir.
/// Tahsisi olan ücret iptal edilemez; önce ilgili ödemeler iptal edilir.
/// </summary>
public sealed record VoidChargeCommand(Guid Id, string? Reason)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Charge.Void";
    public string? AuditTarget => $"ChargeId={Id}";
}
