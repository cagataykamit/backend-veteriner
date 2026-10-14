using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Imports.Commit;

/// <summary>Bir projeksiyon kuyruğunun anlık durumu ve sağlık eşiği.</summary>
public sealed record ImportProjectionPressure(
    string Name,
    bool Enabled,
    int Pending,
    TimeSpan? OldestPendingAge,
    int DegradedAfterSeconds);

/// <summary>Bekleyen outbox ölçümü (genel kuyruk + içe aktarmanın ürettiği projeksiyon olayları).</summary>
public sealed record ImportOutboxPressure(
    int OutboxPending,
    int OutboxPendingDegradedThreshold,
    IReadOnlyList<ImportProjectionPressure> Projections);

/// <summary>Outbox ölçümü; uygulaması altyapıdadır (sağlık kontrolleriyle aynı süzgeç ve ayarlar).</summary>
public interface IImportBackpressureProbe
{
    Task<ImportOutboxPressure> ReadAsync(CancellationToken ct);
}

/// <summary>Beklemeli mi kararının tek yeri (saf, test edilebilir).</summary>
public static class ImportBackpressurePolicy
{
    /// <summary>Beklemek gerekiyorsa nedeni (günlük için; kişisel veri yok), gerekmiyorsa null.</summary>
    public static string? WaitReason(ImportOutboxPressure pressure, ImportBackpressureOptions options)
    {
        if (!options.Enabled)
            return null;

        var ratio = Math.Clamp(options.HeadroomRatio, 0.1, 1.0);
        var outboxLimit = Math.Max(1, (int)Math.Floor(pressure.OutboxPendingDegradedThreshold * ratio));
        if (pressure.OutboxPending >= outboxLimit)
            return $"outbox-pending {pressure.OutboxPending} >= {outboxLimit}";

        foreach (var projection in pressure.Projections.Where(p => p.Enabled))
        {
            if (projection.Pending >= options.ProjectionPendingLimit)
                return $"{projection.Name}-pending {projection.Pending} >= {options.ProjectionPendingLimit}";

            var ageLimit = projection.DegradedAfterSeconds * ratio;
            if (projection.OldestPendingAge is { } age && age.TotalSeconds >= ageLimit)
                return $"{projection.Name}-oldest-age {age.TotalSeconds:F0}s >= {ageLimit:F0}s";
        }

        return null;
    }
}

/// <summary>
/// Parça öncesi bekleme: eşik altına inene kadar <see cref="ImportBackpressureOptions.WaitSeconds"/> aralıkla ölçer ve
/// her turda kirayı yeniler; en çok <see cref="ImportBackpressureOptions.MaxWaitSeconds"/> bekler.
/// </summary>
public sealed class ImportBackpressureGate
{
    private readonly ImportBackpressureOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public ImportBackpressureGate(ImportBackpressureOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <returns>Devam edilebilir mi; kira kaybedildiyse false (işçi durmalı).</returns>
    public async Task<bool> WaitAsync(
        Guid batchId,
        Func<CancellationToken, Task<ImportOutboxPressure>> readPressure,
        Func<CancellationToken, Task<bool>> renewLease,
        CancellationToken ct)
    {
        if (!_options.Enabled)
            return true;

        var startedAt = _timeProvider.GetUtcNow();
        var throttled = false;
        while (true)
        {
            var reason = ImportBackpressurePolicy.WaitReason(await readPressure(ct), _options);
            if (reason is null)
            {
                if (throttled)
                    _logger.LogInformation("Import commit resumed after backpressure. Batch={BatchId}", batchId);
                return true;
            }

            var waited = _timeProvider.GetUtcNow() - startedAt;
            if (waited.TotalSeconds >= _options.MaxWaitSeconds)
            {
                _logger.LogWarning(
                    "Import commit backpressure max wait reached; continuing. Batch={BatchId} Reason={Reason} WaitedSeconds={Waited:F0}",
                    batchId, reason, waited.TotalSeconds);
                return true;
            }

            if (!throttled)
            {
                _logger.LogInformation("Import commit throttled by backpressure. Batch={BatchId} Reason={Reason}", batchId, reason);
                throttled = true;
            }

            if (!await renewLease(ct))
                return false;

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.WaitSeconds)), _timeProvider, ct);
        }
    }
}
