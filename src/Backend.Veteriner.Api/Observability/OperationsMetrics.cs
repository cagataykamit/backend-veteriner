using System.Diagnostics.Metrics;
using Backend.Veteriner.Application.Common.Outbox;

namespace Backend.Veteriner.Api.Observability;

/// <summary>
/// Operasyon metrikleri. Gauge geri çağrıları SQL çalıştırmaz; outbox değerleri son health sonucundan gelir.
/// Etiketler düşük kardinalitelidir (tenant/kullanıcı/kimlik/URL yok).
/// </summary>
public sealed class OperationsMetrics : IDisposable
{
    public const string MeterName = "Backend.Veteriner.Operations";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _unhandledErrors;
    private OutboxStatus _outbox = new(0, 0, null);

    public OperationsMetrics()
    {
        _unhandledErrors = _meter.CreateCounter<long>("ops.errors.unhandled.total", "{error}", "Raporlanan beklenmeyen hata sayısı");
        _meter.CreateObservableGauge("ops.outbox.pending", () => Volatile.Read(ref _outbox).PendingCount, "{message}", "Bekleyen outbox mesajı");
        _meter.CreateObservableGauge("ops.outbox.dead_letter", () => Volatile.Read(ref _outbox).DeadLetterCount, "{message}", "Dead-letter outbox mesajı");
        _meter.CreateObservableGauge("ops.outbox.oldest_pending_age", () => Volatile.Read(ref _outbox).OldestPendingAge?.TotalSeconds ?? 0d, "s", "En eski bekleyen outbox mesajının yaşı");
    }

    public void RecordUnhandledError(Exception exception)
        => _unhandledErrors.Add(1, new KeyValuePair<string, object?>("exception_type", exception.GetType().Name));

    public void UpdateOutbox(OutboxStatus status) => Volatile.Write(ref _outbox, status);

    public void Dispose() => _meter.Dispose();
}
