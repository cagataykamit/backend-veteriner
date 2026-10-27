using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Outbox tablosundaki mesajları periyodik olarak işler.
///
/// Desteklenen mesaj tipleri: e-posta outbox mesajları. Projeksiyon entegrasyon olayları (appointment, client, pet,
/// payment) bu işlemciye girmez; her birinin kendi claim tabanlı işlemcisi vardır. Domain event yayını park edilmiştir
/// (foundation-rules §9); tanınmayan tip dead-letter'a düşer.
///
/// Kurumsal davranış: retry / exponential backoff / jitter, dead-letter desteği.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly OutboxOptions _opt;
    private readonly TimeProvider _timeProvider;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxProcessor(IServiceProvider sp, IOptions<OutboxOptions> opt, TimeProvider timeProvider)
    {
        _sp = sp;
        _opt = opt.Value;
        _timeProvider = timeProvider;
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

                var now = _timeProvider.GetUtcNow().UtcDateTime;

                // Hazir, islenmemis, dead-letter olmayan mesajlar (projection integration eventleri SQL'de haric:
                // appointment + client + pet; bunlarin kendi dedike projection processor'lari vardir).
                var batch = await OutboxMessageQueryFilters
                    .ExcludingProjectionIntegrationEvents(db.OutboxMessages)
                    .Where(m => m.ProcessedAtUtc == null
                             && m.DeadLetterAtUtc == null
                             && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now))
                    .OrderBy(m => m.CreatedAtUtc)
                    .ThenBy(m => m.Id)
                    .Take(Math.Max(1, _opt.BatchSize))
                    .ToListAsync(stoppingToken);

                batch = batch.Where(m => !OutboxMessageQueryFilters.IsProjectionIntegrationEvent(m.Type)).ToList();

                if (batch.Count == 0)
                    continue;

                foreach (var msg in batch)
                {
                    try
                    {
                        await ProcessMessageAsync(msg, scope.ServiceProvider, stoppingToken);

                        OutboxRetryHelper.ApplySuccess(msg, _timeProvider.GetUtcNow().UtcDateTime);
                    }
                    catch (Exception ex)
                    {
                        OutboxRetryHelper.ApplyFailure(msg, _opt, ex, _timeProvider.GetUtcNow().UtcDateTime);

                        if (msg.DeadLetterAtUtc is not null)
                        {
                            Log.Error(ex,
                                "Outbox dead-letter. Type={Type} Id={Id} Retry={Retry}",
                                msg.Type, msg.Id, msg.RetryCount);
                        }
                        else
                        {
                            var backoff = OutboxRetryHelper.ComputeBackoff(_opt.BaseDelaySeconds, msg.RetryCount);
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
                // Stop sırasında iptal normal durum
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Outbox loop error");
            }
        }
    }

    private static async Task ProcessMessageAsync(OutboxMessage msg, IServiceProvider sp, CancellationToken ct)
    {
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

            default:
                throw new NotSupportedException($"Unknown outbox type: {msg.Type}");
        }
    }
}
