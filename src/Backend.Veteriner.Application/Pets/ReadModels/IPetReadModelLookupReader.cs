namespace Backend.Veteriner.Application.Pets.ReadModels;

/// <summary>
/// Pet read-model üzerinden paylaşılan arama/lookup (12D-2). Liste reader'dan ayrı tutulur;
/// handler routing 12D-3/12D-4'te bu interface üzerinden yapılacaktır.
/// </summary>
public interface IPetReadModelLookupReader
{
    /// <summary>
    /// Strateji A / <c>ListSearchPetIds</c> eşdeğeri: pet metin alanları + denormalize client adı.
    /// Pattern null/boş ise boş küme.
    /// </summary>
    Task<PetTextSearchLookupResult> ResolvePetIdsByTextSearchAsync(
        PetTextSearchLookupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Strateji B pet metin araması: yalnız pet kartı alanları (client adı genişlemesi yok).
    /// <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantTextFieldsSearchSpec"/> eşdeğeri.
    /// </summary>
    Task<PetTextFieldsSearchLookupResult> ResolvePetIdsByPetTextFieldsAsync(
        PetTextFieldsSearchLookupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen client id'lere ait tüm pet id'leri.
    /// <see cref="Backend.Veteriner.Application.Pets.Specs.PetsByTenantForClientIdsSpec"/> eşdeğeri.
    /// </summary>
    Task<PetIdsByClientIdsLookupResult> ResolvePetIdsByClientIdsAsync(
        PetIdsByClientIdsLookupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Toplu pet id → pet/client/species görüntüleme bilgisi; yalnız istenen tenant satırları.</summary>
    Task<PetDisplayLookupResult> GetDisplayByIdsAsync(
        PetDisplayLookupRequest request,
        CancellationToken cancellationToken = default);
}
