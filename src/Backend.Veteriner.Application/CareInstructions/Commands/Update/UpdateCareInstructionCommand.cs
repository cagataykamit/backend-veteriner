using Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.CareInstructions.Commands.Update;

/// <summary>Yalnız metin değişir (bağlar sabit); <see cref="RowVersion"/> zorunludur.</summary>
public sealed record UpdateCareInstructionCommand(Guid Id, string Text, string? RowVersion)
    : IRequest<Result<CareInstructionWriteResultDto>>, ITransactionalRequest, IAuditableRequest
{
    public string AuditAction => "CareInstruction.Update";
    public string? AuditTarget => $"CareInstructionId={Id}";
}
