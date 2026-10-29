using System.Linq.Expressions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Kiracıya bağlı müşteri (hayvan sahibi).
/// Telefon opsiyonel; doluysa <see cref="TurkishMobilePhone"/> ile 905XXXXXXXXX saklanır (Phone ve PhoneNormalized aynı).
/// </summary>
public sealed class Client : AggregateRoot, ITenantOwned
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public string FullName { get; private set; } = default!;
    /// <summary>Trim + küçük harf; boşsa null.</summary>
    public string? Email { get; private set; }
    /// <summary>Türkiye cep standardı 12 hane (905XXXXXXXXX) veya null.</summary>
    public string? Phone { get; private set; }
    /// <summary>Serbest metin adres; boş/whitespace ise null.</summary>
    public string? Address { get; private set; }
    /// <summary>Mükerrer (ad+telefon) için; Phone ile aynı standart.</summary>
    public string? PhoneNormalized { get; private set; }

    /// <summary>Peşin müşterinin sabit adı (doğrudan satış, ADR-016); kiracıda tek kayıt.</summary>
    public const string WalkInFullName = "Peşin müşteri";

    /// <summary>Müşterisiz perakende satışın yazıldığı sistem müşterisi; düzenlenemez, kiracıda en fazla bir.</summary>
    public bool IsWalkIn { get; private set; }

    /// <summary>Tercih edilen iletişim kanalı (yalnız bilgi); varsayılan <see cref="ContactChannel.None"/>.</summary>
    public ContactChannel PreferredContactChannel { get; private set; } = ContactChannel.None;

    /// <summary>Müşteri otomatik hatırlatma istemiyor; true iken aşı/randevu hatırlatma adaylarından dışlanır.</summary>
    public bool RemindersOptedOut { get; private set; }

    /// <summary>Otomatik hatırlatma alabilen müşteri koşulu (tek kural; hatırlatma aday sorguları bunu kullanır).</summary>
    public static readonly Expression<Func<Client, bool>> AcceptsReminders = c => !c.RemindersOptedOut;

    /// <summary>KVKK açık rıza formu sonucu (ADR-021); varsayılan <see cref="KvkkConsentStatus.None"/>. Yalnız <see cref="RecordKvkkConsent"/> ile değişir.</summary>
    public KvkkConsentStatus KvkkConsentStatus { get; private set; } = KvkkConsentStatus.None;

    /// <summary>İmzalı formun alındığı gün (İstanbul takvim günü); yalnız Kabul/Ret iken dolu.</summary>
    public DateOnly? KvkkConsentRecordedOn { get; private set; }

    /// <summary>Rıza durumunu işaretleyen kullanıcı; yalnız Kabul/Ret iken dolu.</summary>
    public Guid? KvkkConsentRecordedByUserId { get; private set; }

    /// <summary>
    /// İşaretleme anında geçerli klinik açık rıza metninin sürümü (ADR-032; 0 = taslak varsayılan metin). Yalnız Kabul/Ret iken dolu;
    /// null = sürüm bilinmiyor (ADR-032 öncesi kayıtlar geriye dönük doldurulmaz).
    /// </summary>
    public int? KvkkConsentTemplateVersion { get; private set; }

    /// <summary>İşaretleme anındaki açık rıza metninin SHA-256 özeti (64 karakter hex); <see cref="KvkkConsentTemplateVersion"/> ile birlikte dolu/boş.</summary>
    public string? KvkkConsentTemplateHash { get; private set; }

    public const int MaxTaxOfficeLength = 100;
    public const int MaxLegalNameLength = 300;
    public const int MaxBillingProvinceLength = 80;
    public const int MaxBillingDistrictLength = 80;
    public const int MaxBillingAddressLineLength = 500;

    /// <summary>Fatura alıcısı türü (ADR-036); <c>null</c> = belirtilmemiş. Fatura kimliği alanları yalnız <see cref="ApplyInvoicingIdentity"/> ile değişir.</summary>
    public ClientPersonType? PersonType { get; private set; }

    /// <summary>TCKN (yalnız bireysel); yanıtlarda maskeli döner (<see cref="TurkishTaxIdentity.Mask"/>).</summary>
    public string? NationalId { get; private set; }

    /// <summary>Vergi numarası (yalnız kurumsal): 10 haneli VKN veya şahıs şirketinde 11 haneli TCKN; yanıtlarda maskeli döner.</summary>
    public string? TaxNumber { get; private set; }

    /// <summary>Vergi dairesi (yalnız kurumsal).</summary>
    public string? TaxOffice { get; private set; }

    /// <summary>Unvan (yalnız kurumsal).</summary>
    public string? LegalName { get; private set; }

    /// <summary>Fatura adresi: il.</summary>
    public string? BillingProvince { get; private set; }

    /// <summary>Fatura adresi: ilçe.</summary>
    public string? BillingDistrict { get; private set; }

    /// <summary>Fatura adresi: adres satırı.</summary>
    public string? BillingAddressLine { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private Client() { }

    public static Client CreateWalkIn(Guid tenantId, DateTime utcNow)
        => new(tenantId, WalkInFullName, utcNow) { IsWalkIn = true };

    public Client(Guid tenantId, string fullName, DateTime utcNow, string? phone = null, string? email = null, string? address = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));

        TenantId = tenantId;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = null;
        FullName = fullName.Trim();
        Email = NormalizeEmailForStorage(email);
        ApplyPhone(phone);
        Address = NormalizeAddress(address);
    }

    public void UpdateDetails(string fullName, string? email, string? phone, string? address, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));
        FullName = fullName.Trim();
        Email = NormalizeEmailForStorage(email);
        ApplyPhone(phone);
        Address = NormalizeAddress(address);
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// İletişim tercihlerini günceller; <c>null</c> parametre mevcut değere dokunmaz (PUT tam değiştirme olduğu için
    /// alanı bilmeyen eski istemci tercihi silmesin). Kanal tercihi <see cref="ContactChannel.None"/> ile temizlenir.
    /// </summary>
    public Result ApplyContactPreferences(ContactChannel? preferredChannel, bool? remindersOptedOut)
    {
        if (preferredChannel is { } channel && !Enum.IsDefined(channel))
            return Result.Failure("Clients.Validation", "Geçersiz iletişim kanalı.");

        if (preferredChannel is { } newChannel)
            PreferredContactChannel = newChannel;
        if (remindersOptedOut is { } optedOut)
            RemindersOptedOut = optedOut;
        return Result.Success();
    }

    /// <summary>
    /// Hasta sahibi hatırlatmaları kendisi açabilir mi (APPT-014): KVKK "Ret" kaydı varken hayır. Ret hatırlatmayı kapatır
    /// (ADR-025 madde 4); sahibin portal tercihi klinikteki imzalı rıza kaydının yerine geçmez, bayrağı klinik elle açar.
    /// </summary>
    public bool OwnerCanEnableReminders => KvkkConsentStatus != KvkkConsentStatus.Declined;

    /// <summary>
    /// Hasta sahibinin portaldan kendi hatırlatma tercihi (APPT-014). Personel tercihiyle aynı bayrağı
    /// (<see cref="RemindersOptedOut"/>) değiştirir; aday dışlama yine <see cref="AcceptsReminders"/> ile tek yerdedir.
    /// Kapatma her zaman serbesttir; açma <see cref="OwnerCanEnableReminders"/> değilse reddedilir. Değer değişmiyorsa kayıt
    /// güncellenmez. Kural yalnızca burada yaşar.
    /// </summary>
    public Result SetRemindersOptedOutByOwner(bool optedOut, DateTime utcNow)
    {
        if (!optedOut && !OwnerCanEnableReminders)
        {
            return Result.Failure(
                "Clients.RemindersKvkkDeclined",
                "Klinikteki KVKK açık rıza kaydınız \"Ret\" olduğu için hatırlatmalar açılamaz; açmak için kliniğinizle görüşün.");
        }

        if (RemindersOptedOut == optedOut)
            return Result.Success();

        RemindersOptedOut = optedOut;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Fatura kimliğini tam değiştirir (ADR-036 Dilim 1; tüm alanlar isteğe bağlı). Kişi türü yokken kimlik/vergi alanı,
    /// bireyselde vergi numarası/vergi dairesi/unvan, kurumsalda TCKN alanı verilemez; TCKN doluysa geçerli TCKN, vergi
    /// numarası doluysa geçerli VKN veya (şahıs şirketi) geçerli TCKN olmalıdır (<see cref="TurkishTaxIdentity"/>).
    /// Adres alanları kişi türünden bağımsızdır. Kural yalnızca burada yaşar.
    /// </summary>
    public Result ApplyInvoicingIdentity(
        ClientPersonType? personType,
        string? nationalId,
        string? taxNumber,
        string? taxOffice,
        string? legalName,
        string? billingProvince,
        string? billingDistrict,
        string? billingAddressLine,
        DateTime utcNow)
    {
        if (personType is { } type && !Enum.IsDefined(type))
            return InvoicingInvalid("Geçersiz kişi türü.");

        var tckn = TurkishTaxIdentity.Normalize(nationalId);
        var vkn = TurkishTaxIdentity.Normalize(taxNumber);
        var office = NormalizeOptional(taxOffice);
        var name = NormalizeOptional(legalName);
        var province = NormalizeOptional(billingProvince);
        var district = NormalizeOptional(billingDistrict);
        var addressLine = NormalizeOptional(billingAddressLine);

        var hasCorporateFields = vkn is not null || office is not null || name is not null;
        switch (personType)
        {
            case null when tckn is not null || hasCorporateFields:
                return InvoicingInvalid("Kişi türü seçilmeden kimlik veya vergi bilgisi girilemez.");
            case ClientPersonType.Individual when hasCorporateFields:
                return InvoicingInvalid("Bireysel müşteride VKN, vergi dairesi ve unvan girilmez.");
            case ClientPersonType.Corporate when tckn is not null:
                return InvoicingInvalid("Kurumsal müşteride TCKN alanı kullanılmaz; şahıs şirketi TCKN'sini vergi numarası olarak girin.");
        }

        if (tckn is not null && !TurkishTaxIdentity.IsValidNationalId(tckn))
            return InvoicingInvalid("TCKN geçersiz.");
        if (vkn is not null && !TurkishTaxIdentity.IsValidTaxpayerIdentity(vkn))
            return InvoicingInvalid("Vergi numarası geçersiz (10 haneli VKN veya 11 haneli TCKN).");
        if (office is { Length: > MaxTaxOfficeLength }
            || name is { Length: > MaxLegalNameLength }
            || province is { Length: > MaxBillingProvinceLength }
            || district is { Length: > MaxBillingDistrictLength }
            || addressLine is { Length: > MaxBillingAddressLineLength })
            return InvoicingInvalid("Fatura bilgisi alanı izin verilen uzunluğu aşıyor.");

        PersonType = personType;
        NationalId = tckn;
        TaxNumber = vkn;
        TaxOffice = office;
        LegalName = name;
        BillingProvince = province;
        BillingDistrict = district;
        BillingAddressLine = addressLine;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// KVKK açık rıza formu sonucunu kaydeder (ADR-021). Kabul/Ret: tarih verilmezse bugün, bugünden ileri olamaz;
    /// işaretleyen kullanıcı yazılır. Yok (yanlış girişin geri alınması): tarih verilemez, tarih ve kullanıcı temizlenir.
    /// Ret, hatırlatma istemiyor bayrağını (<see cref="RemindersOptedOut"/>) açar (ADR-025 madde 4); Kabul ve Yok bayrağa dokunmaz,
    /// bayrak yalnız elle kapanır (yanlışlıkla hatırlatma gitmesin diye güvenli taraf).
    /// Kabul/Ret, o anki açık rıza metninin sürümünü ve özetini yazar (ADR-032; ikisi birlikte verilir ya da ikisi de null = bilinmiyor);
    /// Yok bunları da temizler. Kural yalnızca burada yaşar.
    /// </summary>
    public Result RecordKvkkConsent(
        KvkkConsentStatus status,
        DateOnly? recordedOn,
        DateOnly today,
        Guid recordedByUserId,
        int? templateVersion,
        string? templateHash,
        DateTime utcNow)
    {
        switch (status)
        {
            case KvkkConsentStatus.None:
                if (recordedOn.HasValue)
                    return Result.Failure("Clients.Validation", "Rıza durumu 'Yok' iken tarih verilemez.");
                KvkkConsentStatus = KvkkConsentStatus.None;
                KvkkConsentRecordedOn = null;
                KvkkConsentRecordedByUserId = null;
                KvkkConsentTemplateVersion = null;
                KvkkConsentTemplateHash = null;
                break;

            case KvkkConsentStatus.Accepted:
            case KvkkConsentStatus.Declined:
                if (recordedOn > today)
                    return Result.Failure("Clients.Validation", "Rıza tarihi gelecekte olamaz.");
                if (recordedByUserId == Guid.Empty)
                    return Result.Failure("Clients.Validation", "İşaretleyen kullanıcı geçersiz.");
                if (templateVersion.HasValue != (templateHash is not null)
                    || templateVersion < ClinicKvkkFormTemplate.DefaultTextVersion
                    || (templateHash is not null && templateHash.Length != ClinicKvkkFormTemplate.TextHashLength))
                    return Result.Failure("Clients.Validation", "Rıza metni sürümü geçersiz.");
                KvkkConsentStatus = status;
                KvkkConsentRecordedOn = recordedOn ?? today;
                KvkkConsentRecordedByUserId = recordedByUserId;
                KvkkConsentTemplateVersion = templateVersion;
                KvkkConsentTemplateHash = templateHash;
                if (status == KvkkConsentStatus.Declined)
                    RemindersOptedOut = true;
                break;

            default:
                return Result.Failure("Clients.Validation", "Geçersiz rıza durumu.");
        }

        UpdatedAtUtc = utcNow;
        return Result.Success();
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

    private void ApplyPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            Phone = null;
            PhoneNormalized = null;
            return;
        }

        if (!TurkishMobilePhone.TryNormalize(phone, out var norm) || norm is null)
            throw new ArgumentException("Geçersiz Türkiye cep telefonu.", nameof(phone));

        Phone = norm;
        PhoneNormalized = norm;
    }

    /// <summary>Mükerrer kontrol ve saklama ile aynı kural (trim + küçük harf).</summary>
    public static string? NormalizeEmailForStorage(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// Aynı ad + e-posta/telefon mükerreri için tek karşılaştırma anahtarı (yalnız karşılaştırma; saklanan <see cref="FullName"/> olduğu gibi kalır).
    /// Kural <see cref="TurkishTextKey.Normalize"/> içindedir.
    /// </summary>
    public static string NormalizeFullNameForDuplicateCheck(string fullName)
        => TurkishTextKey.Normalize(fullName);

    /// <summary>Okuma modeli sıralama anahtarı (trim + invariant küçük harf). Mükerrer kuralından bağımsızdır; saklı satırlar değişmesin diye eski biçim korunur.</summary>
    public static string NormalizeFullNameForSort(string fullName)
        => string.IsNullOrWhiteSpace(fullName) ? string.Empty : fullName.Trim().ToLowerInvariant();

    private static string? NormalizeAddress(string? address)
        => string.IsNullOrWhiteSpace(address) ? null : address.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result InvoicingInvalid(string message)
        => Result.Failure("Clients.Validation", message);
}
