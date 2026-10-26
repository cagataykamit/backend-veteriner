using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Sales.Commands.Void;

/// <summary>
/// POST /api/v1/sales/{id}/void. <c>Sales.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir.
/// <see cref="StockOutcome"/> (<c>StockReturned</c> varsayılan | <c>WrittenOff</c>): iade seçilirse her satırın çıkışı ters hareketle
/// aynı transaction'da geri verilir, zarar seçilirse stoğa dokunulmaz. Satışın ödemesi ve ücreti de aynı işlemde iptal edilir.
/// </summary>
public sealed record VoidSaleCommand(Guid Id, string? Reason, string? StockOutcome)
    : IRequest<Result<SaleDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Sale.Void";
    public string? AuditTarget => $"SaleId={Id}";
}
