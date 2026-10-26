using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.Rollback;

/// <summary>
/// POST /api/v1/imports/batches/{id}/rollback (IMPORT_API_CONTRACT.md §7.12): partinin oluşturduğu kayıtların geri
/// alınmasını arka plan işine (<c>RollingBack</c>) alır. <see cref="RequestId"/> istek anahtarıdır (tekrar = aynı iş).
/// Audit payload'ı kimlikleri, gerekçeyi ve politikayı taşır (satır verisi yok).
/// </summary>
public sealed record RollbackImportBatchCommand(Guid Id, Guid RequestId, string? Reason, string? BlockedPolicy)
    : IRequest<Result<ImportRollbackResultDto>>, IAuditableRequest
{
    public const int MaxReasonLength = 500;

    public string AuditAction => "ImportBatch.Rollback";
    public string? AuditTarget => $"ImportBatchId={Id}";

    /// <summary>Verilmezse varsayılan <see cref="ImportRollbackPolicy.Fail"/> (ADR-019 K7).</summary>
    public string EffectivePolicy => string.IsNullOrWhiteSpace(BlockedPolicy) ? ImportRollbackPolicy.Fail : BlockedPolicy;
}
