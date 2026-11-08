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
}

/// <summary>
/// Bağlantı sayfası portalın kimliksiz sayfasıdır: <c>{PortalBaseUrl}/randevu-yanit?token=…&amp;islem=onayla|iptal</c>.
/// Sayfayı açmak hiçbir şeyi değiştirmez; eylem sayfadaki düğmeyle POST edilir (e-posta güvenlik tarayıcıları bağlantıyı açar).
/// </summary>
public static class AppointmentOwnerLinkUrls
{
    public const string PagePath = "randevu-yanit";
    public const string ConfirmActionValue = "onayla";
    public const string CancelActionValue = "iptal";

    public static string Page(OwnerPortalOptions portal, AppOptions app, string rawToken, AppointmentOwnerLinkAction action)
        => $"{OwnerPortalEmails.PortalRoot(portal, app)}/{PagePath}?token={Uri.EscapeDataString(rawToken)}"
           + $"&islem={(action == AppointmentOwnerLinkAction.Cancel ? CancelActionValue : ConfirmActionValue)}";
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
        string? clinicPhone)
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

        return body + "Bağlantılar randevu saatine kadar geçerlidir ve yalnız bir kez kullanılabilir.";
    }
}
