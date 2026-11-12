namespace Backend.Veteriner.Application.Imports.Rollback;

/// <summary>
/// Geri almada değerlendirilecek, partinin oluşturduğu kayıt kimlikleri. <see cref="BatchId"/>: partinin kendi kayıtları
/// (randevu, devir ücreti) bağ sayılmasın diye (ADR-048).
/// </summary>
public sealed record ImportRollbackTargets(
    IReadOnlyCollection<Guid> ClientIds,
    IReadOnlyCollection<Guid> PetIds,
    IReadOnlyCollection<Guid> VaccinationIds)
{
    public Guid BatchId { get; init; }
    public IReadOnlyCollection<Guid> ClinicalRecordIds { get; init; } = [];
    public IReadOnlyCollection<Guid> AccountEntryIds { get; init; } = [];
    public IReadOnlyCollection<Guid> AppointmentIds { get; init; } = [];
    public IReadOnlyCollection<Guid> ProductIds { get; init; } = [];
}

/// <summary>Partinin oluşturduğu aşının kendi bağları (silinebilirlik girdisi).</summary>
public sealed record ImportRollbackVaccinationFact(Guid PetId, bool LinkedToExamination, bool HasAdministration, bool Modified);

/// <summary>Partinin oluşturduğu randevunun kendi bağları: durum/saat değişti mi, geliş ya da muayene bağlı mı.</summary>
public sealed record ImportRollbackAppointmentFact(Guid PetId, bool Changed, bool HasVisits, bool HasExaminations);

/// <summary>
/// Partinin müşteriye açtığı devir ücreti ya da devir alacağı (<see cref="IsCredit"/>; ADR-048 T3). Ücrete tahsilat bağlandıysa
/// veya iptal edildiyse; alacaktan mahsup yapıldıysa (geri alınmış olsa da), iade ya da iptal edildiyse <see cref="Changed"/>.
/// </summary>
public sealed record ImportRollbackOpeningBalanceFact(Guid Id, bool Changed, bool IsCredit = false);

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
    IReadOnlyDictionary<Guid, IReadOnlySet<string>> ClientBlockers)
{
    /// <summary>Var olan hedef klinik geçmiş kayıtları → hasta.</summary>
    public IReadOnlyDictionary<Guid, Guid> ClinicalRecords { get; init; } = new Dictionary<Guid, Guid>();

    /// <summary>Hedef hastaların <b>tüm</b> içe aktarılmış klinik geçmiş kayıtları (her parti).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> PetClinicalRecords { get; init; } = new Dictionary<Guid, IReadOnlyList<Guid>>();

    /// <summary>Var olan hedef hesap hareketleri → müşteri.</summary>
    public IReadOnlyDictionary<Guid, Guid> AccountEntries { get; init; } = new Dictionary<Guid, Guid>();

    /// <summary>Hedef müşterilerin <b>tüm</b> içe aktarılmış hesap hareketleri (her parti).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ClientAccountEntries { get; init; } = new Dictionary<Guid, IReadOnlyList<Guid>>();

    /// <summary>Var olan hedef randevular.</summary>
    public IReadOnlyDictionary<Guid, ImportRollbackAppointmentFact> Appointments { get; init; } = new Dictionary<Guid, ImportRollbackAppointmentFact>();

    /// <summary>Hedef müşterilere partinin açtığı devir ücreti ya da alacağı (müşteri → kayıt; müşteride en çok biri).</summary>
    public IReadOnlyDictionary<Guid, ImportRollbackOpeningBalanceFact> OpeningBalances { get; init; } = new Dictionary<Guid, ImportRollbackOpeningBalanceFact>();

    /// <summary>Var olan hedef ürünler → bağ neden kodları (boş küme: silinebilir).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlySet<string>> Products { get; init; } = new Dictionary<Guid, IReadOnlySet<string>>();
}

/// <summary>
/// Hedef kayıtların bağımlılıklarını kiracı içinde, satır sayısından bağımsız sabit sayıda sorguyla okur. Komut tarafında
/// birim işlemiyle aynı <c>DbContext</c>/transaction'da çalışır (denetim ile silme aynı işlemde).
/// </summary>
public interface IImportRollbackDependencyReader
{
    /// <remarks>
    /// <see cref="ImportRollbackFacts.PetOwners"/>: hedef hastalar ve hedef aşıların hastaları için sahip.
    /// <see cref="ImportRollbackFacts.PetVaccinations"/>: hedef hastaların <b>tüm</b> aşıları.
    /// <see cref="ImportRollbackFacts.ClientPets"/>: hedef müşterilerin <b>tüm</b> hastaları. Partinin kendi randevuları ve
    /// devir ücreti hasta/müşteri bağına (<c>HasAppointments</c>, <c>HasCharges</c>) girmez; ayrı olgu olarak gelir.
    /// </remarks>
    Task<ImportRollbackFacts> ReadAsync(Guid tenantId, ImportRollbackTargets targets, CancellationToken ct);
}
