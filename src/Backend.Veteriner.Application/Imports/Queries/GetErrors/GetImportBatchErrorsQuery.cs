using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Queries.GetErrors;

/// <summary>GET /api/v1/imports/batches/{id}/errors: hata raporu dosyası. İndirme audit'e yazılır (satır verisi yok).</summary>
public sealed record GetImportBatchErrorsQuery(Guid Id, string? Format)
    : IRequest<Result<ImportTemplateFileDto>>, IAuditableRequest
{
    public const string Xlsx = "xlsx";
    public const string Csv = "csv";

    public string AuditAction => "ImportBatch.ErrorReport";
    public string? AuditTarget => $"ImportBatchId={Id}; format={Format ?? Xlsx}";
}
