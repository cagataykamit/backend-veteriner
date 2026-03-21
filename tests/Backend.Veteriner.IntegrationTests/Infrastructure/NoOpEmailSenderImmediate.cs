using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Outbox processor integration senaryolarında gerçek SMTP çağrısı yapmaz.
/// </summary>
internal sealed class NoOpEmailSenderImmediate : IEmailSenderImmediate
{
    public Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken ct = default,
        bool isHtml = false)
        => Task.CompletedTask;

    public Task SendAsync(
        string to,
        string subject,
        string body,
        IEnumerable<IEmailAttachment> attachments,
        CancellationToken ct = default,
        bool isHtml = false)
        => Task.CompletedTask;
}
