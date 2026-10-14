using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.ChargeSuggestions.Commands.Convert;

/// <summary>
/// POST /api/v1/charge-suggestions/{id}/convert. <c>Charges.Create</c> gerekir. <see cref="Description"/>, <see cref="Quantity"/>,
/// <see cref="UnitPrice"/> verilmezse kalemdeki değer kullanılır. <see cref="RequestId"/> tekrar korumasıdır (aynı istek ilk ücreti döndürür).
/// Ücret ve kalem durumu tek transaction'da yazılır; bir adım başarısız olursa hiçbiri kalmaz.
/// </summary>
public sealed record ConvertChargeSuggestionCommand(
    Guid Id,
    string? Description,
    decimal? Quantity,
    decimal? UnitPrice,
    Guid? RequestId)
    : IRequest<Result<ChargeCreateResultDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "ChargeSuggestion.Convert";
    public string? AuditTarget => $"ChargeSuggestionId={Id}";
}
