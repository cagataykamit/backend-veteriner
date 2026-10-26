using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;

/// <summary>
/// Bir sahip birimini (müşteri + hastaları + aşıları) tek veritabanı işleminde uygular (IMPORT_API_CONTRACT.md §6.5).
/// Yalnız arka plan uygulama işçisinden, partinin kiracı kapsamında gönderilir; HTTP ucu yoktur. Başarısız sonuç
/// (<see cref="ITransactionalRollbackOnFailureRequest"/>) birimin tüm yazımlarını geri alır. Audit'lenmez: satır verisi
/// kişisel veridir ve payload'a girmemelidir.
/// </summary>
public sealed record ApplyImportOwnerUnitCommand(
    Guid BatchId,
    string LeaseOwner,
    string OwnerKey,
    IReadOnlyList<Guid> RowIds)
    : IRequest<Result<ImportOwnerUnitResult>>, ITransactionalRollbackOnFailureRequest;

/// <param name="AlreadyApplied">Birimin satırları zaten yazılmış (yeniden çalıştırma); hiçbir şey yapılmadı.</param>
public sealed record ImportOwnerUnitResult(bool AlreadyApplied);

/// <summary>Birim hatasının taşıdığı bilgi: başarısız satır ve kiranın kaybı (tek yer).</summary>
public static class ImportOwnerUnitErrors
{
    public const string LeaseLostCode = "Imports.LeaseLost";
    private const string RowIdKey = "rowId";

    public static Error LeaseLost()
        => new(LeaseLostCode, "Partinin uygulama kirası bu işçide değil.");

    public static Error ForRow(Guid rowId, Error error)
        => error with { Extensions = new Dictionary<string, object?> { [RowIdKey] = rowId } };

    public static Guid? RowIdOf(Error error)
        => error.Extensions is not null && error.Extensions.TryGetValue(RowIdKey, out var value) && value is Guid id ? id : null;

    /// <summary>Yarışta mükerrer: birim bir kez yeniden çözülüp denenir (bu kez mevcut kayıtla eşleşir).</summary>
    public static bool IsRetryable(Error error)
        => error.Code is "Clients.DuplicateClient" or "Pets.DuplicatePet";
}
