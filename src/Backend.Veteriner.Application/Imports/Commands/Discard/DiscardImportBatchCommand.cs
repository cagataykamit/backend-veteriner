using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.Discard;

/// <summary>DELETE /api/v1/imports/batches/{id}: taslak parti <c>Discarded</c> olur; ürün verisine dokunulmaz.</summary>
public sealed record DiscardImportBatchCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "ImportBatch.Discard";
    public string? AuditTarget => $"ImportBatchId={Id}";
}
