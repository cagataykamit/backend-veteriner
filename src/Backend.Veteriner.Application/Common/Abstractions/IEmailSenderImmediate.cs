namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Anl�k (outbox�a girmeden) e-posta g�nderimi i�in s�zle�me.
/// </summary>
public interface IEmailSenderImmediate
{
    /// <summary>
    /// Basit (ek dosyas�z) e-posta g�nderimi.
    /// </summary>
    Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken ct = default,
        bool isHtml = false);

    /// <summary>
    /// Ek dosyalarla birlikte e-posta g�nderimi.
    /// </summary>
    Task SendAsync(
        string to,
        string subject,
        string body,
        IEnumerable<IEmailAttachment> attachments,
        CancellationToken ct = default,
        bool isHtml = false);
}
