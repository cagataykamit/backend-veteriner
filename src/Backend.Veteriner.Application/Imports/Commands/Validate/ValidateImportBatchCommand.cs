using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Commands.Validate;

/// <summary>
/// POST /api/v1/imports/batches/{id}/validate: kuru çalıştırmayı kuyruğa alır (ADR-019 K13; iş arka planda sürer). Ürün
/// tablolarına yazmaz. Audit payload'ı yalnız parti kimliğini taşır.
/// </summary>
public sealed record ValidateImportBatchCommand(Guid Id) : IRequest<Result<ImportValidationStartResultDto>>, IAuditableRequest
{
    public string AuditAction => "ImportBatch.Validate";
    public string? AuditTarget => $"ImportBatchId={Id}";
}
