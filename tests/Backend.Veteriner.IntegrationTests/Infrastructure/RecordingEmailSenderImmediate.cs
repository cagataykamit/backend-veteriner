using System.Collections.Concurrent;
using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Outbox işlemci testlerinde gerçek SMTP çağrısı yapmaz; konu başına gönderim denemesini sayar. Konuya bağlanan davranış
/// (bekletme, hata) gönderim sırasında çalışır.
/// </summary>
internal sealed class RecordingEmailSenderImmediate : IEmailSenderImmediate
{
    private readonly ConcurrentDictionary<string, int> _attempts = new();
    private readonly ConcurrentDictionary<string, Func<Task>> _behaviors = new();

    public int AttemptsFor(string subject) => _attempts.GetValueOrDefault(subject);

    public void OnSend(string subject, Func<Task> behavior) => _behaviors[subject] = behavior;

    public Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken ct = default,
        bool isHtml = false)
    {
        _attempts.AddOrUpdate(subject, 1, (_, count) => count + 1);
        return _behaviors.TryGetValue(subject, out var behavior) ? behavior() : Task.CompletedTask;
    }

    public Task SendAsync(
        string to,
        string subject,
        string body,
        IEnumerable<IEmailAttachment> attachments,
        CancellationToken ct = default,
        bool isHtml = false)
        => SendAsync(to, subject, body, ct, isHtml);
}
