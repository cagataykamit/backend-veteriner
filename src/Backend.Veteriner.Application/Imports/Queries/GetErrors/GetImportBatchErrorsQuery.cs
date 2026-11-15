using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetErrors;

/// <summary>
/// GET /api/v1/imports/batches/{id}/errors: hata raporu dosyası; <c>rows=skipped</c> ile bilerek atlanacak satırlar (isteğe
/// bağlı <c>code</c> ile tek neden). İndirme audit'e yazılır (satır verisi yok).
/// </summary>
public sealed record GetImportBatchErrorsQuery(Guid Id, string? Format, string? Rows = null, string? Code = null)
    : IRequest<Result<ImportTemplateFileDto>>, IAuditableRequest
{
    public const string Xlsx = "xlsx";
    public const string Csv = "csv";
    public const string ErrorRows = "errors";
    public const string SkippedRows = "skipped";

    public bool Skipped => string.Equals(Rows, SkippedRows, StringComparison.OrdinalIgnoreCase);

    public string AuditAction => "ImportBatch.ErrorReport";
    public string? AuditTarget => $"ImportBatchId={Id}; format={Format ?? Xlsx}; rows={(Skipped ? SkippedRows : ErrorRows)}";
}
