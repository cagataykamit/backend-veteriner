namespace Backend.Veteriner.Application.Imports.Rollback;

/// <summary>Geri almada değerlendirilecek, partinin oluşturduğu kayıt kimlikleri.</summary>
public sealed record ImportRollbackTargets(
    IReadOnlyCollection<Guid> ClientIds,
    IReadOnlyCollection<Guid> PetIds,
    IReadOnlyCollection<Guid> VaccinationIds);

/// <summary>Partinin oluşturduğu aşının kendi bağları (silinebilirlik girdisi).</summary>
public sealed record ImportRollbackVaccinationFact(Guid PetId, bool LinkedToExamination, bool HasAdministration, bool Modified);

/// <summary>
/// Bağımlılık okuması (IMPORT_API_CONTRACT.md §9). Yalnız <b>var olan</b> hedef kayıtlar sözlüklerde bulunur; olmayan
/// (önceki denemede silinmiş) kayıt planlayıcıda "zaten yok" sayılır. Bağlar neden koduyla gelir
/// (<see cref="ImportRollbackReasons"/>); "başka aşı/hasta" bağı planlayıcıda hesaplanır.
/// </summary>
public sealed record ImportRollbackFacts(
    IReadOnlyDictionary<Guid, ImportRollbackVaccinationFact> Vaccinations,
    IReadOnlyDictionary<Guid, Guid> PetOwners,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> PetVaccinations,
    IReadOnlyDictionary<Guid, IReadOnlySet<string>> PetBlockers,
    IReadOnlyDictionary<Guid, bool> ClientModified,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ClientPets,
    IReadOnlyDictionary<Guid, IReadOnlySet<string>> ClientBlockers);

/// <summary>
/// Hedef kayıtların bağımlılıklarını kiracı içinde, satır sayısından bağımsız sabit sayıda sorguyla okur. Komut tarafında
/// birim işlemiyle aynı <c>DbContext</c>/transaction'da çalışır (denetim ile silme aynı işlemde).
/// </summary>
public interface IImportRollbackDependencyReader
{
    /// <remarks>
    /// <see cref="ImportRollbackFacts.PetOwners"/>: hedef hastalar ve hedef aşıların hastaları için sahip.
    /// <see cref="ImportRollbackFacts.PetVaccinations"/>: hedef hastaların <b>tüm</b> aşıları.
    /// <see cref="ImportRollbackFacts.ClientPets"/>: hedef müşterilerin <b>tüm</b> hastaları.
    /// </remarks>
    Task<ImportRollbackFacts> ReadAsync(Guid tenantId, ImportRollbackTargets targets, CancellationToken ct);
}
