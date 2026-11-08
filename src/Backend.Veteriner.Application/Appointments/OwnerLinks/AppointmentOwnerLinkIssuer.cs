using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Domain.Appointments;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Appointments.OwnerLinks;

/// <summary>Yeni bağlantı (izlenmez; çağıran e-postayla aynı kayıtta ekler) ve e-postaya girecek iki adres.</summary>
public sealed record IssuedAppointmentOwnerLink(AppointmentOwnerLink Link, string ConfirmUrl, string CancelUrl);

/// <summary>
/// Hatırlatma e-postası için "onayla / iptal et" bağlantısı üretir (TIER-019). Token portal davetiyle aynı desendir: 256 bit,
/// URL güvenli; depoya yalnız SHA-256 özeti yazılır, ham token yalnız e-postadadır.
/// </summary>
public sealed class AppointmentOwnerLinkIssuer
{
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public AppointmentOwnerLinkIssuer(
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        IOptions<OwnerPortalOptions> portal,
        IOptions<AppOptions> app)
    {
        _secrets = secrets;
        _hash = hash;
        _portal = portal.Value;
        _app = app.Value;
    }

    public IssuedAppointmentOwnerLink Issue(Guid tenantId, Guid clinicId, Guid appointmentId, DateTime appointmentStartUtc, DateTime utcNow)
    {
        var rawToken = _secrets.CreateToken();
        var link = AppointmentOwnerLink.Create(tenantId, clinicId, appointmentId, _hash.ComputeSha256(rawToken), appointmentStartUtc, utcNow);
        return new IssuedAppointmentOwnerLink(
            link,
            AppointmentOwnerLinkUrls.Page(_portal, _app, rawToken, AppointmentOwnerLinkAction.Confirm),
            AppointmentOwnerLinkUrls.Page(_portal, _app, rawToken, AppointmentOwnerLinkAction.Cancel));
    }

    /// <summary>
    /// "Takvime ekle" (TIER-020): .ics indiren kimliksiz uca giden takvim bağlantısı. Aynı token deseni ve tablo; amaç
    /// <see cref="AppointmentOwnerLinkPurpose.Calendar"/> olduğundan eylem yapamaz. API kökü (<c>App:BaseUrl</c>) boşsa
    /// bağlantı üretilmez (<c>null</c>); e-postada yalnız Google Takvim bağlantısı kalır.
    /// </summary>
    public IssuedAppointmentCalendarLink? IssueCalendar(Guid tenantId, Guid clinicId, Guid appointmentId, DateTime appointmentStartUtc, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(_app.BaseUrl))
            return null;

        var rawToken = _secrets.CreateToken();
        var link = AppointmentOwnerLink.Create(
            tenantId, clinicId, appointmentId, _hash.ComputeSha256(rawToken), appointmentStartUtc, utcNow, AppointmentOwnerLinkPurpose.Calendar);
        return new IssuedAppointmentCalendarLink(link, AppointmentOwnerLinkUrls.CalendarFile(_app, rawToken));
    }

    /// <summary>
    /// Bekleme listesi teklifinin kabul bağlantısı (TIER-018): aynı token deseni ve tablo, amaç
    /// <see cref="AppointmentOwnerLinkPurpose.WaitlistOffer"/>. Sayfayı açmak hiçbir şeyi değiştirmez; kabul ve ret POST'tur.
    /// </summary>
    public IssuedWaitlistOfferLink IssueWaitlistOffer(
        Guid tenantId,
        Guid clinicId,
        Guid sourceAppointmentId,
        Guid offerId,
        DateTime offerExpiresAtUtc,
        DateTime utcNow)
    {
        var rawToken = _secrets.CreateToken();
        var link = AppointmentOwnerLink.CreateForWaitlistOffer(
            tenantId, clinicId, sourceAppointmentId, offerId, _hash.ComputeSha256(rawToken), offerExpiresAtUtc, utcNow);
        return new IssuedWaitlistOfferLink(link, AppointmentOwnerLinkUrls.WaitlistOfferPage(_portal, _app, rawToken));
    }
}

/// <summary>Yeni teklif bağlantısı (izlenmez; çağıran e-postayla aynı kayıtta ekler) ve portal sayfası adresi.</summary>
public sealed record IssuedWaitlistOfferLink(AppointmentOwnerLink Link, string PageUrl);

/// <summary>Yeni takvim bağlantısı (izlenmez; çağıran e-postayla aynı kayıtta ekler) ve .ics adresi.</summary>
public sealed record IssuedAppointmentCalendarLink(AppointmentOwnerLink Link, string IcsUrl);

/// <summary>
/// Bağlantı sayfası portalın kimliksiz sayfasıdır: <c>{PortalBaseUrl}/randevu-yanit?token=…&amp;islem=onayla|iptal</c>.
/// Sayfayı açmak hiçbir şeyi değiştirmez; eylem sayfadaki düğmeyle POST edilir (e-posta güvenlik tarayıcıları bağlantıyı açar).
/// </summary>
public static class AppointmentOwnerLinkUrls
{
    public const string PagePath = "randevu-yanit";
    public const string ConfirmActionValue = "onayla";
    public const string CancelActionValue = "iptal";

    /// <summary>Kimliksiz .ics ucu (API kökünden; <c>PortalAppointmentLinkController</c>).</summary>
    public const string CalendarFilePath = "api/portal/v1/appointment-link/calendar.ics";

    public static string Page(OwnerPortalOptions portal, AppOptions app, string rawToken, AppointmentOwnerLinkAction action)
        => $"{OwnerPortalEmails.PortalRoot(portal, app)}/{PagePath}?token={Uri.EscapeDataString(rawToken)}"
           + $"&islem={(action == AppointmentOwnerLinkAction.Cancel ? CancelActionValue : ConfirmActionValue)}";

    /// <summary>Bekleme listesi teklifi (TIER-018): <c>{PortalBaseUrl}/bekleme-teklifi?token=…</c>.</summary>
    public const string WaitlistOfferPagePath = "bekleme-teklifi";

    public static string WaitlistOfferPage(OwnerPortalOptions portal, AppOptions app, string rawToken)
        => $"{OwnerPortalEmails.PortalRoot(portal, app)}/{WaitlistOfferPagePath}?token={Uri.EscapeDataString(rawToken)}";

    /// <summary><c>{App:BaseUrl}/api/portal/v1/appointment-link/calendar.ics?token=…</c>; GET yalnız dosya döner.</summary>
    public static string CalendarFile(AppOptions app, string rawToken)
        => $"{app.BaseUrl.TrimEnd('/')}/{CalendarFilePath}?token={Uri.EscapeDataString(rawToken)}";
}

/// <summary>
/// Randevu hatırlatma e-postasının metni (TIER-019 ile bağlantılı). Hizmet bilgilendirmesidir, ticari ileti değildir
/// (HUKUKI-ONAY-LISTESI A4 varsayımı): tanıtım içermez.
/// </summary>
public static class AppointmentReminderEmail
{
    public const string Subject = "Randevu hatırlatması";

    public static string Body(
        string recipientName,
        string petName,
        string clinicName,
        string whenText,
        IssuedAppointmentOwnerLink links,
        bool cancelLinkEnabled,
        string? clinicPhone,
        string calendarLines)
    {
        var body =
            $"Sayın {recipientName},\n" +
            $"{petName} için {clinicName} kliniğinde {whenText} tarihinde planlanan randevunuzu hatırlatırız.\n\n" +
            $"Geleceğinizi onaylamak için:\n{links.ConfirmUrl}\n\n";

        body += cancelLinkEnabled
            ? $"Gelemeyecekseniz randevunuzu iptal etmek için:\n{links.CancelUrl}\n\n"
            : string.IsNullOrWhiteSpace(clinicPhone)
                ? "Gelemeyecekseniz iptal için lütfen kliniği arayın.\n\n"
                : $"Gelemeyecekseniz iptal için lütfen kliniği arayın: {clinicPhone.Trim()}\n\n";

        // TIER-020: takvim satırları (Google Takvim ve .ics; AppointmentCalendar.EmailLines).
        return body + "Onay ve iptal bağlantıları randevu saatine kadar geçerlidir ve yalnız bir kez kullanılabilir.\n\n" + calendarLines;
    }
}
