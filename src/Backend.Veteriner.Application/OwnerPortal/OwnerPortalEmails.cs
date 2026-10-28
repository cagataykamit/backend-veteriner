using System.Text;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal;

/// <summary>
/// Portal e-postalarının metni ve davet bağlantısı (ADR-031; v0 bildirim kanalı e-posta, AK-6). Metin minimum veri taşır:
/// klinik adı ve kod/bağlantı; hayvan veya sağlık bilgisi yoktur (K7).
/// </summary>
public static class OwnerPortalEmails
{
    public sealed record Message(string Subject, string Body);

    /// <summary>Davet bağlantısı: <c>{PortalBaseUrl}/invite?token=…</c>; kök boşsa <c>{FrontendBaseUrl}/portal</c>.</summary>
    public static string InviteLink(OwnerPortalOptions portal, AppOptions app, string rawToken)
    {
        var root = string.IsNullOrWhiteSpace(portal.PortalBaseUrl)
            ? $"{(app.FrontendBaseUrl ?? string.Empty).TrimEnd('/')}/portal"
            : portal.PortalBaseUrl.TrimEnd('/');
        return $"{root}/invite?token={Uri.EscapeDataString(rawToken)}";
    }

    public static Message Invite(string clinicName, string link, int expiryDays) => new(
        $"{clinicName} sizi hasta sahibi portalına davet ediyor",
        new StringBuilder()
            .AppendLine("Merhaba,")
            .AppendLine()
            .AppendLine($"{clinicName}, hayvanlarınızın randevu, aşı ve hesap bilgilerini görebileceğiniz hasta sahibi portalına sizi davet ediyor.")
            .AppendLine()
            .AppendLine("Daveti kabul etmek için aşağıdaki bağlantıyı açın:")
            .AppendLine(link)
            .AppendLine()
            .AppendLine($"Bu bağlantı {expiryDays} gün geçerlidir ve yalnız bir kez kullanılabilir.")
            .AppendLine()
            .AppendLine("Bu daveti beklemiyorsanız e-postayı dikkate almayabilirsiniz.")
            .AppendLine()
            .AppendLine("Vetinity")
            .ToString());

    public static Message Code(OwnerPortalCodePurpose purpose, string code)
    {
        var (subject, intro) = purpose switch
        {
            OwnerPortalCodePurpose.PasswordReset => ("Vetinity parola sıfırlama kodunuz", "Hasta sahibi portalı parolanızı sıfırlamak için kodunuz:"),
            OwnerPortalCodePurpose.AccountDeletion => ("Vetinity hesap silme onay kodunuz", "Hasta sahibi portalı hesabınızı silmek için onay kodunuz:"),
            _ => ("Vetinity giriş kodunuz", "Hasta sahibi portalına giriş kodunuz:"),
        };

        return new Message(
            subject,
            new StringBuilder()
                .AppendLine("Merhaba,")
                .AppendLine()
                .AppendLine(intro)
                .AppendLine()
                .AppendLine(code)
                .AppendLine()
                .AppendLine($"Kod {(int)OwnerPortalCode.Lifetime.TotalMinutes} dakika geçerlidir ve yalnız bir kez kullanılabilir. Kodu kimseyle paylaşmayın.")
                .AppendLine()
                .AppendLine("Bu işlemi siz başlatmadıysanız e-postayı dikkate almayabilirsiniz.")
                .AppendLine()
                .AppendLine("Vetinity")
                .ToString());
    }

    /// <summary>Hesap silindikten sonra eski adrese giden tek onay (ADR-041 K8); klinik adı ve hayvan bilgisi taşımaz.</summary>
    public static Message AccountDeleted() => new(
        "Vetinity portal hesabınız silindi",
        new StringBuilder()
            .AppendLine("Merhaba,")
            .AppendLine()
            .AppendLine("Hasta sahibi portalı hesabınız isteğiniz üzerine silindi. E-posta adresiniz, parolanız ve oturumlarınız silindi; kliniklerle portal bağlantılarınız kapatıldı.")
            .AppendLine()
            .AppendLine("Klinikteki hayvan, muayene, aşı ve ödeme kayıtları kliniğin sorumluluğundadır ve bu işlemle silinmez. Bu kayıtlarla ilgili talepleriniz için kliniğinize başvurabilirsiniz.")
            .AppendLine()
            .AppendLine("Portalı yeniden kullanmak isterseniz kliniğinizden yeni davet isteyebilirsiniz.")
            .AppendLine()
            .AppendLine("Vetinity")
            .ToString());
}
