using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Administrations.Commands.Void;

/// <summary>
/// POST /api/v1/administrations/{id}/void. <c>Administrations.Correct</c> yetkisi ve <see cref="Reason"/> (5–500 karakter) gerekir.
/// Stok, ters hareketle aynı transaction'da geri verilir.
/// </summary>
public sealed record VoidAdministrationCommand(Guid Id, string? Reason)
    : IRequest<Result<AdministrationDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Administration.Void";
    public string? AuditTarget => $"AdministrationId={Id}";
}
