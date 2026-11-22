using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Personel davet e-postası (davet oluşturma ve "Yeniden gönder"). Metin yalnız klinik adı, bağlantı ve son geçerlilik zamanını taşır.
/// Gönderim <c>IEmailSender</c> (outbox) ile davet kaydıyla aynı kayıtta yazılır.
/// </summary>
public static class TenantInviteEmails
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public sealed record Message(string Subject, string Body);

    /// <summary>Panelin davete katılım sayfası: <c>{App:FrontendBaseUrl}/join/{token}</c>.</summary>
    public static string JoinLink(AppOptions app, string rawToken)
        => $"{(app.FrontendBaseUrl ?? string.Empty).TrimEnd('/')}/join/{Uri.EscapeDataString(rawToken)}";

    public static Message Invite(string clinicName, string link, DateTime expiresAtUtc) => new(
        $"{clinicName} sizi Vetinity'de ekibine davet ediyor",
        new StringBuilder()
            .AppendLine("Merhaba,")
            .AppendLine()
            .AppendLine($"{clinicName} sizi Vetinity klinik yönetim panelinde ekibine davet ediyor.")
            .AppendLine()
            .AppendLine("Daveti kabul etmek ve hesabınızı oluşturmak için aşağıdaki bağlantıyı açın:")
            .AppendLine(link)
            .AppendLine()
            .AppendLine($"Bu bağlantı {OperationDayBounds.ToLocal(expiresAtUtc).ToString("dd.MM.yyyy HH:mm", Turkish)} tarihine kadar geçerlidir ve yalnız bir kez kullanılabilir.")
            .AppendLine()
            .AppendLine("Bu daveti beklemiyorsanız e-postayı dikkate almayabilirsiniz.")
            .AppendLine()
            .AppendLine("Vetinity")
            .ToString());
}
