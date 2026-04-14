using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using MediatR;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Outbox tablosundaki mesajlar� periyodik olarak i�ler.
/// 
/// Desteklenen mesaj tipleri:
/// - Email outbox mesajlar�
/// - Domain event outbox mesajlar�
/// 
/// Kurumsal davran��:
/// - Retry / exponential backoff / jitter
/// - Dead-letter deste�i
/// - Email ve domain event ak��lar�n� tek i�lemcide toplama
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly OutboxOptions _opt;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxProcessor(IServiceProvider sp, IOptions<OutboxOptions> opt)
    {
        _sp = sp;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            Log.Information("OutboxProcessor disabled via Outbox:Enabled=false; background outbox polling skipped.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _opt.LoopIntervalSeconds));
        var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;

                // Haz�r, i�lenmemi�, dead-letter olmayan mesajlar
                var batch = await db.OutboxMessages
                    .Where(m => m.ProcessedAtUtc == null
                             && m.DeadLetterAtUtc == null
                             && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now))
                    .OrderBy(m => m.CreatedAtUtc)
                    .Take(Math.Max(1, _opt.BatchSize))
                    .ToListAsync(stoppingToken);

                if (batch.Count == 0)
                    continue;

                foreach (var msg in batch)
                {
                    try
                    {
                        await ProcessMessageAsync(msg, scope.ServiceProvider, stoppingToken);

                        msg.ProcessedAtUtc = DateTime.UtcNow;
                        msg.LastError = null;
                        msg.Error = null;
                        msg.NextAttemptAtUtc = null;
                    }
                    catch (Exception ex)
                    {
                        msg.RetryCount++;
                        msg.LastError = ex.Message;
                        msg.Error = ex.ToString();

                        if (msg.RetryCount >= _opt.MaxRetryCount)
                        {
                            msg.DeadLetterAtUtc = DateTime.UtcNow;

                            Log.Error(ex,
                                "Outbox dead-letter. Type={Type} Id={Id} Retry={Retry}",
                                msg.Type, msg.Id, msg.RetryCount);
                        }
                        else
                        {
                            var backoff = ComputeBackoff(_opt.BaseDelaySeconds, msg.RetryCount);
                            msg.NextAttemptAtUtc = DateTime.UtcNow.Add(backoff);

                            Log.Warning(ex,
                                "Outbox retry in {DelaySeconds}s. Type={Type} Id={Id} Retry={Retry}",
                                (int)backoff.TotalSeconds, msg.Type, msg.Id, msg.RetryCount);
                        }
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Stop s�ras�nda iptal normal durum
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Outbox loop error");
            }
        }
    }

    private static TimeSpan ComputeBackoff(int baseDelaySec, int retry)
    {
        // exponential backoff: base * 2^(retry-1), max ~10 dk
        var baseDelay = Math.Max(1, baseDelaySec);
        var seconds = Math.Min(baseDelay * (int)Math.Pow(2, retry - 1), 600);

        // jitter �20%
        var jitterFactor = (Random.Shared.NextDouble() * 0.4) - 0.2;
        var withJitter = seconds + seconds * jitterFactor;

        return TimeSpan.FromSeconds(Math.Max(1, (int)withJitter));
    }

    private static async Task ProcessMessageAsync(OutboxMessage msg, IServiceProvider sp, CancellationToken ct)
    {
        // 1) Klasik uygulama outbox mesajlar�
        switch (msg.Type)
        {
            case OutboxMessageTypes.Email:
            case OutboxMessageTypes.EmailLegacy:
                {
                    var payload = JsonSerializer.Deserialize<EmailOutboxPayload>(msg.Payload, JsonOptions)
                                  ?? throw new InvalidOperationException("Invalid email payload");

                    var smtp = sp.GetRequiredService<IEmailSenderImmediate>();
                    await smtp.SendAsync(payload.To, payload.Subject, payload.Body, ct, payload.IsHtml);
                    return;
                }
        }

        // 2) Domain event outbox mesajlar�
        var eventRegistry = sp.GetRequiredService<DomainEventTypeRegistry>();
        var mediator = sp.GetRequiredService<IMediator>();

        var eventType = eventRegistry.Resolve(msg.Type);
        if (eventType is not null)
        {
            var domainEvent = JsonSerializer.Deserialize(msg.Payload, eventType, JsonOptions) as IDomainEvent;

            if (domainEvent is null)
                throw new InvalidOperationException($"Domain event deserialize edilemedi. Type={msg.Type}");

            await mediator.Publish(domainEvent, ct);
            return;
        }

        // 3) Desteklenmeyen tip
        throw new NotSupportedException($"Unknown outbox type: {msg.Type}");
    }
}