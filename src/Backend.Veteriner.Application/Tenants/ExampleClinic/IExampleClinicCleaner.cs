using Backend.Veteriner.Application.Tenants.Contracts.Dtos;

namespace Backend.Veteriner.Application.Tenants.ExampleClinic;

/// <summary>
/// Örnek Veteriner Kliniği verisini bulur ve kalıcı siler (EXAMPLE_CLINIC_API_CONTRACT.md §10). "Hangi kayıt örnek"
/// kuralı (işaret, örnek müşteri/hastaya bağlılık, kullanılmış ürünün pasife alınması) burada tek yerdedir; yetki ve
/// kiracı eşleşmesi çağıran komut/sorguda kalır. <see cref="ClearAsync"/> çağıranın işlem (transaction) sınırında çalışır.
/// </summary>
public interface IExampleClinicCleaner
{
    /// <summary>Yan etkisiz: örnek veri var mı ve temizlik neyi silecek.</summary>
    Task<ExampleClinicStatusDto> GetStatusAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Örnek veriyi siler, kullanılmış örnek ürünleri pasife alır; okuma modeli olaylarını aynı işlemde yazar.</summary>
    Task<ExampleClinicDataCountsDto> ClearAsync(Guid tenantId, CancellationToken ct);
}
