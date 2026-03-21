namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default, bool isHtml = false);
}
    