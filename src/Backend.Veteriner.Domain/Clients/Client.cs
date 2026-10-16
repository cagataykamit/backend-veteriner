using System.Linq.Expressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clients;

/// <summary>
/// Kiracıya bağlı müşteri (hayvan sahibi).
/// Telefon opsiyonel; doluysa <see cref="TurkishMobilePhone"/> ile 905XXXXXXXXX saklanır (Phone ve PhoneNormalized aynı).
/// </summary>
public sealed class Client : AggregateRoot
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

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private Client() { }

    public static Client CreateWalkIn(Guid tenantId)
        => new(tenantId, WalkInFullName) { IsWalkIn = true };

    public Client(Guid tenantId, string fullName, string? phone = null, string? email = null, string? address = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));

        TenantId = tenantId;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
        FullName = fullName.Trim();
        Email = NormalizeEmailForStorage(email);
        ApplyPhone(phone);
        Address = NormalizeAddress(address);
    }

    public void UpdateDetails(string fullName, string? email, string? phone, string? address)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Ad soyad boş olamaz.", nameof(fullName));
        FullName = fullName.Trim();
        Email = NormalizeEmailForStorage(email);
        ApplyPhone(phone);
        Address = NormalizeAddress(address);
        UpdatedAtUtc = DateTime.UtcNow;
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
    /// KVKK açık rıza formu sonucunu kaydeder (ADR-021). Kabul/Ret: tarih verilmezse bugün, bugünden ileri olamaz;
    /// işaretleyen kullanıcı yazılır. Yok (yanlış girişin geri alınması): tarih verilemez, tarih ve kullanıcı temizlenir.
    /// Ret, hatırlatma istemiyor bayrağını (<see cref="RemindersOptedOut"/>) açar (ADR-025 madde 4); Kabul ve Yok bayrağa dokunmaz,
    /// bayrak yalnız elle kapanır (yanlışlıkla hatırlatma gitmesin diye güvenli taraf). Kural yalnızca burada yaşar.
    /// </summary>
    public Result RecordKvkkConsent(KvkkConsentStatus status, DateOnly? recordedOn, DateOnly today, Guid recordedByUserId)
    {
        switch (status)
        {
            case KvkkConsentStatus.None:
                if (recordedOn.HasValue)
                    return Result.Failure("Clients.Validation", "Rıza durumu 'Yok' iken tarih verilemez.");
                KvkkConsentStatus = KvkkConsentStatus.None;
                KvkkConsentRecordedOn = null;
                KvkkConsentRecordedByUserId = null;
                break;

            case KvkkConsentStatus.Accepted:
            case KvkkConsentStatus.Declined:
                if (recordedOn > today)
                    return Result.Failure("Clients.Validation", "Rıza tarihi gelecekte olamaz.");
                if (recordedByUserId == Guid.Empty)
                    return Result.Failure("Clients.Validation", "İşaretleyen kullanıcı geçersiz.");
                KvkkConsentStatus = status;
                KvkkConsentRecordedOn = recordedOn ?? today;
                KvkkConsentRecordedByUserId = recordedByUserId;
                if (status == KvkkConsentStatus.Declined)
                    RemindersOptedOut = true;
                break;

            default:
                return Result.Failure("Clients.Validation", "Geçersiz rıza durumu.");
        }

        UpdatedAtUtc = DateTime.UtcNow;
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
}
