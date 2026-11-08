using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.OwnerPortal.MarketingOptOut;

/// <summary>Yeni ret bağlantısı (izlenmez; çağıran e-postayla aynı kayıtta ekler) ve e-postaya girecek adres.</summary>
public sealed record IssuedMarketingOptOutLink(MarketingOptOutLink Link, string Url);

/// <summary>
/// Ticari iletideki ret bağlantısını üretir (ADR-046 K3). Token TIER-019 hatırlatma bağlantısıyla aynı desendir: 256 bit, URL güvenli;
/// depoya yalnız SHA-256 özeti yazılır. Sayfa portalın kimliksiz <c>{PortalBaseUrl}/ticari-ileti-ret?token=…</c> sayfasıdır; açmak hiçbir
/// şeyi değiştirmez, ret sayfadaki düğmeyle POST edilir.
/// </summary>
public sealed class MarketingOptOutLinkIssuer
{
    public const string PagePath = "ticari-ileti-ret";

    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public MarketingOptOutLinkIssuer(
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

    public IssuedMarketingOptOutLink Issue(Guid tenantId, Guid clinicId, Guid ownerAccountClientLinkId, DateTime utcNow)
    {
        var rawToken = _secrets.CreateToken();
        var link = MarketingOptOutLink.Create(tenantId, clinicId, ownerAccountClientLinkId, _hash.ComputeSha256(rawToken), utcNow);
        return new IssuedMarketingOptOutLink(
            link,
            $"{OwnerPortalEmails.PortalRoot(_portal, _app)}/{PagePath}?token={Uri.EscapeDataString(rawToken)}");
    }
}

/// <summary>
/// "Uzun süredir gelmedi" ticari iletisinin metni (ADR-046 K3) — ticari iletinin kimlik ve ret alt bilgisi tek yerde. Konu satırı
/// niteliği belirtir ("Tanıtım", İYS araştırması A5 madde 4); metinde kliniğin adı, varsa telefonu ve ücretsiz, tek adımlık ret
/// bağlantısı bulunur. Tutar, teşhis ve tedavi yazılmaz.
/// </summary>
public static class NoReturnMarketingEmail
{
    public const string SubjectPrefix = "Tanıtım: ";

    public static string Subject(string clinicName) => $"{SubjectPrefix}{clinicName} sizi bekliyor";

    public static string Body(
        string recipientName,
        string petName,
        string clinicName,
        string lastVisitText,
        string? clinicPhone,
        string optOutUrl)
        => $"Sayın {recipientName},\n" +
           $"{petName} son olarak {lastVisitText} tarihinde {clinicName} kliniğine geldi. Kontrol için randevu almak isterseniz kliniğinizle görüşebilirsiniz.\n\n" +
           Footer(clinicName, clinicPhone, optOutUrl);

    /// <summary>Her ticari iletinin sonundaki kimlik ve ret bilgisi.</summary>
    public static string Footer(string clinicName, string? clinicPhone, string optOutUrl)
        => $"Bu ticari elektronik ileti, onay vermeniz üzerine {clinicName} tarafından gönderilmiştir." +
           (string.IsNullOrWhiteSpace(clinicPhone) ? string.Empty : $" İletişim: {clinicPhone.Trim()}.") +
           $"\nTicari ileti almak istemiyorsanız ücretsiz ve tek adımda reddedebilirsiniz:\n{optOutUrl}";
}
