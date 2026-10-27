using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Projections;
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
/// Mesajlar işlenmeden önce kısa süreli claim (lease) ile sahiplenilir (<see cref="SqlGenericOutboxClaimRepository"/>);
/// iki instance aynı mesajı almaz. Her mesajın sonucu gönderimden hemen sonra kendi UPDATE'iyle yazılır; çökme yalnız
/// sonucu yazılmamış mesajları lease süresi dolunca yeniden denemeye bırakır.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private const int MaxWorkerIdLength = 128;

    private readonly IServiceProvider _sp;
    private readonly OutboxOptions _opt;
    private readonly TimeProvider _timeProvider;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxProcessor(IServiceProvider sp, IOptions<OutboxOptions> opt, TimeProvider timeProvider)
    {
        _sp = sp;
        _opt = opt.Value;
        _timeProvider = timeProvider;

        var workerId = $"outbox:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        WorkerId = workerId.Length <= MaxWorkerIdLength ? workerId : workerId[..MaxWorkerIdLength];
    }

    /// <summary>Claim sahibi kimliği (instance başına tekil).</summary>
    public string WorkerId { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            Log.Information("OutboxProcessor disabled via Outbox:Enabled=false; background outbox polling skipped.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _opt.LoopIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
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

    /// <summary>Bir parti mesajı claim eder ve sırayla işler.</summary>
    /// <returns>Başarıyla işlenen mesaj sayısı.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _sp.CreateScope();
        var claims = scope.ServiceProvider.GetRequiredService<SqlGenericOutboxClaimRepository>();

        var claimed = await claims.ClaimNextBatchAsync(
            WorkerId,
            Math.Max(1, _opt.BatchSize),
            TimeSpan.FromSeconds(Math.Max(1, _opt.ClaimLeaseSeconds)),
            cancellationToken);

        var processedCount = 0;
        foreach (var msg in claimed)
        {
            try
            {
                await ProcessMessageAsync(msg, scope.ServiceProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Kalan claim'ler lease süresi dolunca yeniden alınır.
                throw;
            }
            catch (Exception ex)
            {
                await MarkFailureAsync(claims, msg, ex);
                continue;
            }

            // Gönderim yapıldı: kapanışta iptal edilip mesajın yeniden gönderilmemesi için sonuç iptalsiz yazılır.
            if (await claims.MarkProcessedAsync(msg.Id, msg.ClaimToken, WorkerId, CancellationToken.None))
                processedCount++;
            else
                Log.Warning("Outbox completion rejected (claim lost). Type={Type} Id={Id}", msg.Type, msg.Id);
        }

        return processedCount;
    }

    private async Task MarkFailureAsync(SqlGenericOutboxClaimRepository claims, ClaimedIntegrationOutboxMessage msg, Exception ex)
    {
        var retryCount = msg.RetryCount + 1;
        if (retryCount >= _opt.MaxRetryCount)
        {
            if (await claims.MarkDeadLetterAsync(msg.Id, msg.ClaimToken, WorkerId, ex.Message, CancellationToken.None))
                Log.Error(ex, "Outbox dead-letter. Type={Type} Id={Id} Retry={Retry}", msg.Type, msg.Id, retryCount);
            else
                Log.Warning("Outbox dead-letter rejected (claim lost). Type={Type} Id={Id}", msg.Type, msg.Id);
            return;
        }

        var backoff = OutboxRetryHelper.ComputeBackoff(_opt.BaseDelaySeconds, retryCount);
        var nextAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime.Add(backoff);
        if (await claims.MarkRetryAsync(msg.Id, msg.ClaimToken, WorkerId, retryCount, nextAttemptAtUtc, ex.Message, CancellationToken.None))
        {
            Log.Warning(ex,
                "Outbox retry in {DelaySeconds}s. Type={Type} Id={Id} Retry={Retry}",
                (int)backoff.TotalSeconds, msg.Type, msg.Id, retryCount);
        }
        else
        {
            Log.Warning("Outbox retry rejected (claim lost). Type={Type} Id={Id}", msg.Type, msg.Id);
        }
    }

    private static async Task ProcessMessageAsync(ClaimedIntegrationOutboxMessage msg, IServiceProvider sp, CancellationToken ct)
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
