using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.RollbackOwnerUnit;

/// <summary>
/// Bir sahip biriminin (müşteri + partinin oluşturduğu hastaları/aşıları) geri alınması: bağımlılık denetimi ve silme tek
/// veritabanı işleminde (IMPORT_API_CONTRACT.md §7.12, §9). Yalnız arka plan geri alma işçisinden, partinin kiracı
/// kapsamında gönderilir; HTTP ucu yoktur. Kira kaybında başarısız sonuç (<see cref="ITransactionalRollbackOnFailureRequest"/>)
/// birimin tüm yazımlarını geri alır. Audit'lenmez (iş tamamlanma kaydı sayıları taşır).
/// </summary>
public sealed record RollbackImportOwnerUnitCommand(
    Guid BatchId,
    string LeaseOwner,
    IReadOnlyList<Guid> RowIds,
    string Policy)
    : IRequest<Result<ImportRollbackUnitResult>>, ITransactionalRollbackOnFailureRequest;

/// <param name="AlreadyDone">Birimin satırları zaten sonuçlanmış (yeniden çalıştırma); hiçbir şey yapılmadı.</param>
public sealed record ImportRollbackUnitResult(bool AlreadyDone, int Deleted, int Kept);
