using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Commands.Commit;

/// <summary>
/// POST /api/v1/imports/batches/{id}/commit (IMPORT_API_CONTRACT.md §7.9): önizlenen doğrulamayı onaylar ve partiyi
/// arka plan uygulamasına (<c>Committing</c>) alır. <see cref="ValidationId"/> istek anahtarıdır (tekrar = aynı parti).
/// Audit payload'ı yalnız kimlikler ve seçeneği taşır.
/// </summary>
public sealed record CommitImportBatchCommand(Guid Id, Guid ValidationId, string? OnErrors)
    : IRequest<Result<ImportCommitResultDto>>, IAuditableRequest
{
    public const string SkipErrorRows = "SkipErrorRows";
    public const string FailIfAny = "FailIfAny";

    public static readonly IReadOnlyList<string> AllowedOnErrors = [SkipErrorRows, FailIfAny];

    public string AuditAction => "ImportBatch.Commit";
    public string? AuditTarget => $"ImportBatchId={Id}";
}
