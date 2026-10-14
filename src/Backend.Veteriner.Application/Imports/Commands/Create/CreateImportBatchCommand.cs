using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Reading;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Commands.Create;

/// <summary>
/// POST /api/v1/imports/batches: dosyayı okur, parti ve hazırlık satırlarını tek işlemde yazar (<c>Uploaded</c>).
/// Audit payload'ı yalnız <see cref="RequestId"/>, <see cref="Name"/>, <see cref="ClinicId"/>, <see cref="Encoding"/>,
/// <see cref="Delimiter"/> taşır; dosya içeriği (<see cref="Files"/>) kişisel veri olduğundan serileştirilmez.
/// </summary>
public sealed record CreateImportBatchCommand(
    Guid RequestId,
    string? Name,
    Guid? ClinicId,
    [property: JsonIgnore] ImportFileSet Files,
    string? Encoding,
    string? Delimiter)
    : IRequest<Result<ImportBatchCreateResultDto>>, IAuditableRequest, ITransactionalRequest
{
    public string AuditAction => "ImportBatch.Create";
    public string? AuditTarget => $"RequestId={RequestId}";
}
