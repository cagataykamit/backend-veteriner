using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Commands.Void;

/// <summary>
/// POST /api/v1/administrations/{id}/void. <c>Administrations.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir.
/// <see cref="StockOutcome"/> (<c>StockReturned</c> varsayılan | <c>WrittenOff</c>): iade seçilirse stok ters hareketle aynı transaction'da geri verilir,
/// zarar seçilirse stoğa dokunulmaz.
/// </summary>
public sealed record VoidAdministrationCommand(Guid Id, string? Reason, string? StockOutcome)
    : IRequest<Result<AdministrationDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Administration.Void";
    public string? AuditTarget => $"AdministrationId={Id}";
}
