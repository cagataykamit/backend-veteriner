using Backend.Veteriner.Application.Imports.Contracts.Dtos;

namespace Backend.Veteriner.Application.Imports.Rollback;

/// <summary>
/// Engelli kayıtları kullanıcıya tanıtan bilgileri (ad, tür, hasta, sahip, tarih) kiracı içinde, kayıt sayısından bağımsız
/// sabit sayıda sorguyla okur. Kişisel veri taşır: yalnız yetkili kullanıcıya döner, günlüğe yazılmaz. Anahtar hedef kayıt
/// kimliğidir; olmayan kayıt sözlükte bulunmaz.
/// </summary>
public interface IImportRollbackRecordReader
{
    Task<IReadOnlyDictionary<Guid, ImportRollbackRecordDto>> ReadAsync(
        Guid tenantId, IReadOnlyCollection<ImportRollbackItem> items, CancellationToken ct);
}
