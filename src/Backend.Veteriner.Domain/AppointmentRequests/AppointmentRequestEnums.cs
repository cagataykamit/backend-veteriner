namespace Backend.Veteriner.Domain.AppointmentRequests;

/// <summary>Talep yaşam döngüsü (ADR-042 K4). Sayısal değerler stabildir.</summary>
public enum AppointmentRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,

    /// <summary>Sahip vazgeçti.</summary>
    Cancelled = 3,

    /// <summary>İstenen saat geçti; arka plan işi kapattı.</summary>
    Expired = 4,
}

/// <summary>Talebin giriş kapısı (ADR-042 K1).</summary>
public enum AppointmentRequestSource
{
    /// <summary>Oturum açmış hasta sahibi (portal).</summary>
    Portal = 0,

    /// <summary>Kliniğe özel bağlantı; e-posta koduyla doğrulanmış kimliksiz kişi.</summary>
    Link = 1,
}

/// <summary>
/// Talebin kaynağı (TIER-016): klinik talebin nereden geldiğini istatistik olarak görür. Sunucu belirler, istemci serbest
/// değer yazamaz (<see cref="AppointmentRequestChannels"/>). Eski taleplerde boştur ("bilinmiyor"). Kişisel veri değildir;
/// yönlendiren adres, IP ya da tarayıcı bilgisi tutulmaz. Sayısal değerler stabildir.
/// </summary>
public enum AppointmentRequestChannel
{
    /// <summary>Kliniğe özel bağlantı doğrudan açıldı (ya da bağlantıya bilinmeyen kaynak değeriyle gelindi).</summary>
    ClinicLink = 0,

    /// <summary>Kliniğin kendi sitesi; site bağlantıya <c>?kaynak=site</c> ekler.</summary>
    ClinicSite = 1,

    /// <summary>Oturum açmış hasta sahibi (portal).</summary>
    OwnerPortal = 2,

    /// <summary>Telefon ya da elle, klinik personelinin girdiği talep. Personel talep girme yolu yok; henüz üretilmez.</summary>
    Phone = 3,

    /// <summary>Veteriner bul (TIER-015); sayfa randevu bağlantısına <c>?kaynak=veteriner-bul</c> ekler.</summary>
    VetFinder = 4,

    /// <summary>
    /// Kliniğin Vetinity dışındaki sitesine koyduğu düğme ya da gömme kodu (APPT-020/021); panelin verdiği kod bağlantıya
    /// <c>?kaynak=dis-site</c> ekler.
    /// </summary>
    ExternalSite = 5,
}

/// <summary>
/// Bağlantı formunun taşıdığı kaynak değeri → kaynak; izinli değerler tek yer burası. Bilinmeyen, boş ya da başka kapıya
/// ait değer klinik bağlantısı sayılır. Değer kimliksiz istemciden geldiği için kötüye kullanılabilir; yalnız istatistiği
/// etkiler (talep kuralları kaynağa bakmaz).
/// </summary>
public static class AppointmentRequestChannels
{
    /// <summary>Kliniğin sitesinden gelen bağlantının değeri.</summary>
    public const string ClinicSiteToken = "site";

    /// <summary>Veteriner bul kartından gelen bağlantının değeri (TIER-015).</summary>
    public const string VetFinderToken = "veteriner-bul";

    /// <summary>Panelin verdiği HTML düğme ve gömme kodunun değeri (APPT-020/021).</summary>
    public const string ExternalSiteToken = "dis-site";

    public static AppointmentRequestChannel ForLink(string? token)
        => token?.Trim().ToLowerInvariant() switch
        {
            ClinicSiteToken => AppointmentRequestChannel.ClinicSite,
            VetFinderToken => AppointmentRequestChannel.VetFinder,
            ExternalSiteToken => AppointmentRequestChannel.ExternalSite,
            _ => AppointmentRequestChannel.ClinicLink,
        };
}

/// <summary>Durum geçişini yapan.</summary>
public enum AppointmentRequestActorKind
{
    Owner = 0,
    Staff = 1,
    System = 2,
}
