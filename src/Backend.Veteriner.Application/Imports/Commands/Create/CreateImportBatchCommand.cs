using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Commands.Create;

/// <summary>
/// POST /api/v1/imports/batches. B1: dosyasız parti kaydı (<c>Uploaded</c>, satırsız); dosya okuma B2'de eklenir.
/// Audit payload'ı yalnız <see cref="RequestId"/>, <see cref="Name"/>, <see cref="ClinicId"/> taşır (satır verisi yok).
/// </summary>
public sealed record CreateImportBatchCommand(Guid RequestId, string? Name, Guid? ClinicId)
    : IRequest<Result<ImportBatchCreateResultDto>>, IAuditableRequest
{
    public string AuditAction => "ImportBatch.Create";
    public string? AuditTarget => $"RequestId={RequestId}";
}
