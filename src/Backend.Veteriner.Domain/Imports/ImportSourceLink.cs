using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Imports;

/// <summary>Kaynak kimliği bağının kayıt türü.</summary>
public enum ImportSourceEntity
{
    Client = 1,
    Pet = 2,
}

/// <summary>
/// Kaynak program kimliği bağı (ADR-051): eski programın kendi numarası (E-vet protokol no) uygulanan aktarmada oluşan ya da
/// eşleşen kayda bağlanır; ikinci yedekte eşleşme önce bu bağla yapılır. Anahtar kiracı + partinin kliniği + kaynak program +
/// kayıt türü + numaradır (aynı kiracının iki kliniği ayrı program kullanabilir). Satır verisinin temizlenmesinden etkilenmez;
/// geri almada silinmez (hedefi olmayan bağ okurken yok sayılır, sonraki aktarmada <see cref="Retarget"/> ile yenilenir).
/// </summary>
public sealed class ImportSourceLink : ITenantOwned
{
    public const int MaxSourceKeyLength = 100;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public ImportSourceProfile SourceProfile { get; private set; }
    public ImportSourceEntity Entity { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public Guid TargetId { get; private set; }

    /// <summary>Bağı son yazan parti.</summary>
    public Guid BatchId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private ImportSourceLink() { }

    /// <summary>
    /// Kalıcı kaynak anahtarı; kullanılamıyorsa null (kural tek yer). Yalnız program profillerinde (<see cref="ImportSourceProfile.Generic"/>
    /// dışı): genel şablondaki numara kullanıcının dosyaya verdiği geçici numara olabilir. E-vet dönüştürücüsü tekrar eden protokolü
    /// <c>no#satır</c> diye ayırır; satır sırası sonraki yedekte değiştiği için bu anahtar kalıcı değildir.
    /// </summary>
    public static string? StableKey(ImportSourceProfile profile, string? reference)
    {
        if (profile == ImportSourceProfile.Generic || string.IsNullOrWhiteSpace(reference))
            return null;

        var key = reference.Trim();
        return key.Contains('#') || key.Length > MaxSourceKeyLength ? null : key;
    }

    public static ImportSourceLink Create(
        Guid tenantId,
        Guid clinicId,
        ImportSourceProfile profile,
        ImportSourceEntity entity,
        string sourceKey,
        Guid targetId,
        Guid batchId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || targetId == Guid.Empty || batchId == Guid.Empty)
            throw new ArgumentException("Kiracı, klinik, hedef ve parti kimliği boş olamaz.");
        if (!Enum.IsDefined(entity))
            throw new ArgumentOutOfRangeException(nameof(entity));
        if (StableKey(profile, sourceKey) != sourceKey)
            throw new ArgumentException("Kaynak anahtarı kalıcı değil.", nameof(sourceKey));

        return new ImportSourceLink
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            SourceProfile = profile,
            Entity = entity,
            SourceKey = sourceKey,
            TargetId = targetId,
            BatchId = batchId,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };
    }

    /// <summary>Bağı yeni hedefe (önceki hedef silinmiş ya da başka kayıtla eşleşmiş) ve yazan partiye taşır; hedef aynıysa değişmez.</summary>
    public void Retarget(Guid targetId, Guid batchId, DateTime nowUtc)
    {
        if (targetId == Guid.Empty || batchId == Guid.Empty)
            throw new ArgumentException("Hedef ve parti kimliği boş olamaz.");
        if (TargetId == targetId)
            return;

        TargetId = targetId;
        BatchId = batchId;
        UpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    }
}
