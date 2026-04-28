using Backend.Veteriner.Application.Common.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Serilog;

namespace Backend.Veteriner.Infrastructure.Mailing;

public sealed class MailKitEmailSender : IEmailSenderImmediate
{
    private readonly SmtpOptions _opt;

    public MailKitEmailSender(IOptions<SmtpOptions> options)
        => _opt = options.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Basit (ek dosyas�z) e-posta g�nderimi.
    /// </summary>
    public async Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken ct = default,
        bool isHtml = false)
    {
        await SendAsync(to, subject, body, Array.Empty<IEmailAttachment>(), ct, isHtml);
    }

    /// <summary>
    /// Ek dosyalarla birlikte e-posta g�nderimi.
    /// </summary>
    public async Task SendAsync(
        string to,
        string subject,
        string body,
        IEnumerable<IEmailAttachment> attachments,
        CancellationToken ct = default,
        bool isHtml = false)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Al�c� adresi bo� olamaz.", nameof(to));

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_opt.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject ?? string.Empty;

        var builder = new BodyBuilder
        {
            HtmlBody = isHtml ? body ?? string.Empty : null,
            TextBody = !isHtml ? body ?? string.Empty : null
        };

        // ?? Ekleri ekle
        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                if (att?.Content?.Length > 0)
                {
                    var mimePart = new MimePart(att.ContentType)
                    {
                        Content = new MimeContent(new MemoryStream(att.Content)),
                        FileName = att.FileName,
                        ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                        ContentTransferEncoding = ContentEncoding.Base64
                    };
                    builder.Attachments.Add(mimePart);
                }
            }
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();

        // Port bazlı güvenli bağlantı seçimi:
        // - 587: STARTTLS
        // - 465: SSL on connect
        // Diğer portlarda mevcut option davranışını koru.
        var secure = _opt.Port switch
        {
            587 => SecureSocketOptions.StartTls,
            465 => SecureSocketOptions.SslOnConnect,
            _ => _opt.EnableSsl
                ? SecureSocketOptions.StartTls
                : (_opt.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None)
        };

        var hasPassword = !string.IsNullOrWhiteSpace(_opt.Pass);
        var passwordLength = _opt.Pass?.Length ?? 0;
        Log.Debug(
            "SMTP runtime options Host={Host} Port={Port} User={User} From={From} Secure={Secure} PasswordPresent={PasswordPresent} PasswordLength={PasswordLength}",
            _opt.Host,
            _opt.Port,
            _opt.User ?? string.Empty,
            _opt.From,
            secure,
            hasPassword,
            passwordLength);

        await client.ConnectAsync(_opt.Host, _opt.Port, secure, ct);

        if (!string.IsNullOrWhiteSpace(_opt.User) && !string.IsNullOrWhiteSpace(_opt.Pass))
            await client.AuthenticateAsync(_opt.User, _opt.Pass, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
