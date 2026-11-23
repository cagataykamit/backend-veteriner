using System.Linq.Expressions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Pets;

/// <summary>
/// Kiracı ve müşteriye bağlı evcil hayvan kaydı.
/// Tür <see cref="SpeciesId"/> ile global <see cref="Catalog.Species"/> tablosuna bağlıdır.
/// Irk şimdilik serbest metin (<see cref="Breed"/>); ileride opsiyonel <c>BreedId</c> eklenebilir.
/// </summary>
public sealed class Pet : AggregateRoot, ITenantOwned, IChangeTracked
{
    public const int MaxAlertNoteLength = 200;
    public const int MaxHbsAnimalIdLength = 50;

    /// <summary>
    /// Aynı müşteri + ad + tür mükerreri için karşılaştırma anahtarı (yalnız karşılaştırma; saklanan <see cref="Name"/> değişmez).
    /// Müşteri adıyla aynı tek kural: <see cref="Clients.Client.NormalizeFullNameForDuplicateCheck"/>.
    /// </summary>
    public static string NormalizeNameForDuplicateCheck(string name)
        => Clients.Client.NormalizeFullNameForDuplicateCheck(name);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public string Name { get; private set; } = default!;
    public Guid SpeciesId { get; private set; }
    public string? Breed { get; private set; }
    public Guid? ColorId { get; private set; }

    /// <summary>Kürk deseni (tekir, kaplan çizgili…); renk kataloğunda <see cref="PetColorKind.Pattern"/> satırına bağlı, opsiyonel.</summary>
    public Guid? PatternId { get; private set; }

    /// <summary>Global ırk kaydı (FK); serbest metin <see cref="Breed"/> (string) ile birlikte kullanılabilir.</summary>
    public Guid? BreedId { get; private set; }

    public PetGender? Gender { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    /// <summary>Klinik kayıtlarda hayvanın ağırlığı (ör. kilogram); opsiyonel.</summary>
    public decimal? Weight { get; private set; }

    /// <summary>Klinik notları / veteriner gözlemleri; opsiyonel, serbest metin.</summary>
    public string? Notes { get; private set; }

    /// <summary>Mikroçip numarası; opsiyonel.</summary>
    public string? MicrochipNumber { get; private set; }

    /// <summary>Karne / küpe numarası; opsiyonel.</summary>
    public string? PassportOrTagNumber { get; private set; }

    /// <summary>Özel protokol numarası; opsiyonel.</summary>
    public string? SpecialProtocolNumber { get; private set; }

    /// <summary>HBS (Hayvan Bilgi Sistemi) hayvan kimlik numarası (ADR-036); opsiyonel serbest metin. Yalnız <see cref="ApplyHbsAnimalId"/> ile değişir.</summary>
    public string? HbsAnimalId { get; private set; }

    /// <summary>Kısırlaştırılmış mı; varsayılan false.</summary>
    public bool IsNeutered { get; private set; }

    /// <summary>Kalıcı klinik güvenlik uyarıları; varsayılan <see cref="PetAlertFlags.None"/> (uyarı yok).</summary>
    public PetAlertFlags AlertFlags { get; private set; }

    /// <summary>Uyarılar için tek kısa not (ör. alerjinin ne olduğu); yalnızca en az bir bayrak varken dolu olabilir.</summary>
    public string? AlertNote { get; private set; }

    /// <summary>Yaşam durumu; varsayılan <see cref="PetStatus.Active"/>. Yalnız <see cref="SetStatus"/> ile değişir.</summary>
    public PetStatus Status { get; private set; } = PetStatus.Active;

    /// <summary>Vefat tarihi (opsiyonel); yalnız <see cref="PetStatus.Deceased"/> iken dolu olabilir.</summary>
    public DateOnly? DeceasedOn { get; private set; }

    /// <summary>Yumuşak silme anı (ADR-054); doluysa kayıt listelerden, aramadan ve hatırlatmalardan çıkar, geçmiş kayıtlar korunur.</summary>
    public DateTime? DeletedAtUtc { get; private set; }

    /// <summary>Silen kullanıcı; yalnız <see cref="DeletedAtUtc"/> doluyken dolu.</summary>
    public Guid? DeletedByUserId { get; private set; }

    /// <summary>Müşteriyle birlikte silindi (ADR-054 K4); müşteri geri yüklenince bu hastalar da geri gelir.</summary>
    public bool DeletedWithClient { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    /// <summary>Silinmemiş hasta koşulu (tek kural; liste, arama, seçici ve sayım sorguları bunu kullanır, ADR-054 K2).</summary>
    public static readonly Expression<Func<Pet, bool>> NotDeleted = p => p.DeletedAtUtc == null;

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    /// <summary>
    /// Bakım iş akışına giren hayvan koşulu (tek kural): hatırlatma adayları ve gecikmiş/yaklaşan aşı listeleri bu ifadeyle
    /// süzülür; yeni randevu/geliş/muayene kapısı <see cref="EnsureCanStartCare"/> aynı koşulu kullanır. Vefat eden ve
    /// silinen (ADR-054) hayvan bakıma girmez.
    /// </summary>
    public static readonly Expression<Func<Pet, bool>> ReceivesCare = p => p.Status == PetStatus.Active && p.DeletedAtUtc == null;

    private static readonly Func<Pet, bool> ReceivesCareCompiled = ReceivesCare.Compile();

    /// <summary>Bellekteki kayıt için <see cref="ReceivesCare"/> (hasta özeti ve açık işler gibi tek hayvanlı okumalar).</summary>
    public bool IsReceivingCare => ReceivesCareCompiled(this);

    /// <summary>Okuma/projeksiyon için (EF ilişkisi).</summary>
    public Species? Species { get; private set; }

    /// <summary>Okuma/projeksiyon için (EF ilişkisi).</summary>
    public Breed? BreedRef { get; private set; }
    /// <summary>Okuma/projeksiyon için (EF ilişkisi).</summary>
    public PetColor? ColorRef { get; private set; }
    /// <summary>Okuma/projeksiyon için (EF ilişkisi).</summary>
    public PetColor? PatternRef { get; private set; }

    private Pet() { }

    public Pet(
        Guid tenantId,
        Guid clientId,
        string name,
        Guid speciesId,
        string? breed = null,
        DateOnly? birthDate = null,
        Guid? breedId = null,
        PetGender? gender = null,
        Guid? colorId = null,
        decimal? weight = null,
        string? notes = null,
        string? microchipNumber = null,
        string? passportOrTagNumber = null,
        string? specialProtocolNumber = null,
        bool isNeutered = false,
        Guid? patternId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clientId == Guid.Empty)
            throw new ArgumentException("ClientId geçersiz.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hayvan adı boş olamaz.", nameof(name));
        if (speciesId == Guid.Empty)
            throw new ArgumentException("SpeciesId geçersiz.", nameof(speciesId));

        TenantId = tenantId;
        ClientId = clientId;
        Name = name.Trim();
        SpeciesId = speciesId;
        Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim();
        BirthDate = birthDate;
        BreedId = breedId;
        Gender = gender;
        ColorId = colorId;
        PatternId = patternId;
        Weight = weight;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        MicrochipNumber = NormalizeOptionalIdentityNumber(microchipNumber);
        PassportOrTagNumber = NormalizeOptionalIdentityNumber(passportOrTagNumber);
        SpecialProtocolNumber = NormalizeOptionalIdentityNumber(specialProtocolNumber);
        IsNeutered = isNeutered;
    }

    public void UpdateDetails(
        string name,
        Guid speciesId,
        string? breed,
        DateOnly? birthDate,
        Guid? breedId = null,
        PetGender? gender = null,
        Guid? colorId = null,
        decimal? weight = null,
        string? notes = null,
        string? microchipNumber = null,
        string? passportOrTagNumber = null,
        string? specialProtocolNumber = null,
        bool isNeutered = false,
        Guid? patternId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hayvan adı boş olamaz.", nameof(name));
        if (speciesId == Guid.Empty)
            throw new ArgumentException("SpeciesId geçersiz.", nameof(speciesId));
        Name = name.Trim();
        SpeciesId = speciesId;
        Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim();
        BirthDate = birthDate;
        BreedId = breedId;
        Gender = gender;
        ColorId = colorId;
        PatternId = patternId;
        Weight = weight;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        MicrochipNumber = NormalizeOptionalIdentityNumber(microchipNumber);
        PassportOrTagNumber = NormalizeOptionalIdentityNumber(passportOrTagNumber);
        SpecialProtocolNumber = NormalizeOptionalIdentityNumber(specialProtocolNumber);
        IsNeutered = isNeutered;
    }

    /// <summary>
    /// Sistem kataloğu düzeltmesi: ırk yerine kullanılmış kürk adını (ör. "Tekir") ırk + renk/desen olarak yeniden
    /// sınıflar. Hekimin girdiği renk ve desen korunur; yalnız boş olanlar doldurulur.
    /// </summary>
    public void ReclassifyCoatFromBreed(Guid breedId, string breedName, Guid? colorId, Guid? patternId)
    {
        BreedId = breedId;
        Breed = breedName;
        ColorId ??= colorId;
        PatternId ??= patternId;
    }

    /// <summary>
    /// Uyarıları günceller; <c>null</c> parametre mevcut değere dokunmaz (PUT tam değiştirme olduğu için eski istemci uyarıyı silmesin).
    /// Boş küme notu temizler; bayraksız dolu not reddedilir. Kural yalnızca burada yaşar.
    /// </summary>
    public Result ApplyAlerts(IReadOnlyList<string>? flagNames, string? note)
    {
        var flags = AlertFlags;
        if (flagNames is not null && !PetAlertFlagNames.TryParse(flagNames, out flags))
            return Result.Failure("Pets.Validation", "Geçersiz uyarı bayrağı.");

        var newNote = AlertNote;
        if (note is not null)
            newNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (newNote is { Length: > MaxAlertNoteLength })
            return Result.Failure("Pets.Validation", $"Uyarı notu en çok {MaxAlertNoteLength} karakter olabilir.");

        if (flags == PetAlertFlags.None)
        {
            if (note is not null && !string.IsNullOrWhiteSpace(note))
                return Result.Failure("Pets.Validation", "Not için en az bir uyarı bayrağı gerekir.");

            newNote = null;
        }

        AlertFlags = flags;
        AlertNote = newNote;
        return Result.Success();
    }

    /// <summary>
    /// HBS hayvan kimlik numarasını yazar; <c>null</c> mevcut değere dokunmaz (PUT tam değiştirme olduğu için alanı bilmeyen
    /// eski istemci silmesin), boş/whitespace temizler. En çok <see cref="MaxHbsAnimalIdLength"/> karakter.
    /// </summary>
    public Result ApplyHbsAnimalId(string? hbsAnimalId)
    {
        if (hbsAnimalId is null)
            return Result.Success();

        var value = NormalizeOptionalIdentityNumber(hbsAnimalId);
        if (value is { Length: > MaxHbsAnimalIdLength })
            return Result.Failure("Pets.Validation", $"HBS kimlik numarası en çok {MaxHbsAnimalIdLength} karakter olabilir.");

        HbsAnimalId = value;
        return Result.Success();
    }

    /// <summary>
    /// Durumu değiştirir. Vefat: tarih opsiyonel, bugünden ileri ve doğum tarihinden önce olamaz; zaten vefat ise tarih düzeltilir.
    /// Aktif (yanlış girişin geri alınması): tarih verilemez, vefat tarihi temizlenir. Kural yalnızca burada yaşar.
    /// </summary>
    public Result SetStatus(PetStatus status, DateOnly? deceasedOn, DateOnly today)
    {
        switch (status)
        {
            case PetStatus.Active:
                if (deceasedOn.HasValue)
                    return Result.Failure("Pets.Validation", "Aktif hayvan için vefat tarihi verilemez.");
                Status = PetStatus.Active;
                DeceasedOn = null;
                return Result.Success();

            case PetStatus.Deceased:
                if (deceasedOn > today)
                    return Result.Failure("Pets.Validation", "Vefat tarihi gelecekte olamaz.");
                if (deceasedOn.HasValue && BirthDate.HasValue && deceasedOn.Value < BirthDate.Value)
                    return Result.Failure("Pets.Validation", "Vefat tarihi doğum tarihinden önce olamaz.");
                Status = PetStatus.Deceased;
                DeceasedOn = deceasedOn;
                return Result.Success();

            default:
                return Result.Failure("Pets.Validation", "Geçersiz hayvan durumu.");
        }
    }

    /// <summary>
    /// Yeni randevu, geliş ve muayene açılmadan önce çağrılır; silinen (<c>Pets.Deleted</c>) ya da vefat eden hayvanda reddeder
    /// (geçmiş kayıtlar etkilenmez).
    /// </summary>
    public Result EnsureCanStartCare()
    {
        if (IsReceivingCare)
            return Result.Success();

        return IsDeleted
            ? EnsureNotDeleted()
            : Result.Failure("Pets.Deceased", "Vefat etmiş hayvan için yeni randevu, geliş veya muayene açılamaz.");
    }

    /// <summary>
    /// Yumuşak silme (ADR-054 K1/K5): aktif yatış ya da gelecek randevu varsa (<paramref name="blockers"/>) nedenlerin tamamıyla
    /// reddedilir; zaten silinmişse değişiklik yapılmaz. Vefat etmiş hayvan silinebilir. Kural yalnızca burada yaşar.
    /// </summary>
    public Result Delete(Guid deletedByUserId, DateTime utcNow, RecordDeletionBlockers blockers)
    {
        if (IsDeleted)
            return Result.Success();
        if (deletedByUserId == Guid.Empty)
            return Result.Failure("Pets.Validation", "Silen kullanıcı geçersiz.");
        if (blockers.IsBlocking)
            return Result.Failure(blockers.ToError("Pets.DeleteBlocked", "Hasta"));

        MarkDeleted(deletedByUserId, utcNow, withClient: false);
        return Result.Success();
    }

    /// <summary>
    /// Müşteri silinirken (ADR-054 K4) çağrılır; engeller müşteri düzeyinde tüm hastalar için denetlenmiştir.
    /// Önceden tek başına silinmiş hastaya dokunulmaz.
    /// </summary>
    public void DeleteWithClient(Guid deletedByUserId, DateTime utcNow)
    {
        if (IsDeleted)
            return;

        MarkDeleted(deletedByUserId, utcNow, withClient: true);
    }

    /// <summary>
    /// Silinmiş hastayı geri yükler (ADR-054 K6); müşterisi silinmişken reddedilir. Silinmemişse değişiklik yapılmaz.
    /// </summary>
    public Result Restore(bool clientIsDeleted)
    {
        if (!IsDeleted)
            return Result.Success();
        if (clientIsDeleted)
            return Result.Failure("Pets.ClientDeleted", "Hastanın müşterisi silinmiş; önce müşteriyi geri yükleyin.");

        ClearDeletion();
        return Result.Success();
    }

    /// <summary>Müşteri geri yüklenince yalnız onunla birlikte silinen hastalar geri gelir (ADR-054 K6).</summary>
    public void RestoreWithClient()
    {
        if (IsDeleted && DeletedWithClient)
            ClearDeletion();
    }

    /// <summary>Silinen hastaya yeni iş ve düzenleme yazılmaz (ADR-054 K3).</summary>
    public Result EnsureNotDeleted()
        => IsDeleted
            ? Result.Failure("Pets.Deleted", "Hasta silinmiş; işlem için önce \"Silinenler\" listesinden geri yükleyin.")
            : Result.Success();

    private void MarkDeleted(Guid deletedByUserId, DateTime utcNow, bool withClient)
    {
        DeletedAtUtc = utcNow;
        DeletedByUserId = deletedByUserId;
        DeletedWithClient = withClient;
    }

    private void ClearDeletion()
    {
        DeletedAtUtc = null;
        DeletedByUserId = null;
        DeletedWithClient = false;
    }

    /// <summary>
    /// İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.
    /// </summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private static string? NormalizeOptionalIdentityNumber(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
