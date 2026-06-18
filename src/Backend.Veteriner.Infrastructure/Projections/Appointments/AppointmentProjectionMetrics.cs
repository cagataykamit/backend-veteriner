using System.Diagnostics.Metrics;
using Backend.Veteriner.Application.Projections.Appointments;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionMetrics : IDisposable
{
    public const string MeterName = "Vetinity.Cqrs.Appointments";

    private readonly Meter _meter;
    private readonly Counter<long> _batchesTotal;
    private readonly Counter<long> _eventsProcessedTotal;
    private readonly Counter<long> _eventsFailedTotal;
    private readonly Counter<long> _eventsDeadLetteredTotal;
    private readonly Counter<long> _rebuildsTotal;
    private readonly Histogram<double> _batchDurationMs;
    private readonly Histogram<double> _eventLagMs;
    private readonly Histogram<double> _batchSize;
    private readonly Histogram<double> _rebuildDurationMs;
    private readonly AppointmentProjectionMetricsSnapshotHolder _snapshotHolder;

    public AppointmentProjectionMetrics(AppointmentProjectionMetricsSnapshotHolder snapshotHolder)
    {
        _snapshotHolder = snapshotHolder;
        _meter = new Meter(MeterName);

        _batchesTotal = _meter.CreateCounter<long>(
            "appointment_projection.batches.total",
            unit: "{batch}",
            description: "Appointment projection batches processed.");

        _eventsProcessedTotal = _meter.CreateCounter<long>(
            "appointment_projection.events.processed.total",
            unit: "{event}",
            description: "Appointment projection events processed successfully.");

        _eventsFailedTotal = _meter.CreateCounter<long>(
            "appointment_projection.events.failed.total",
            unit: "{event}",
            description: "Appointment projection events that failed processing.");

        _eventsDeadLetteredTotal = _meter.CreateCounter<long>(
            "appointment_projection.events.dead_lettered.total",
            unit: "{event}",
            description: "Appointment projection events moved to dead-letter.");

        _rebuildsTotal = _meter.CreateCounter<long>(
            "appointment_projection.rebuilds.total",
            unit: "{rebuild}",
            description: "Appointment projection rebuild operations.");

        _batchDurationMs = _meter.CreateHistogram<double>(
            "appointment_projection.batch.duration",
            unit: "ms",
            description: "Appointment projection batch duration.");

        _eventLagMs = _meter.CreateHistogram<double>(
            "appointment_projection.event.lag",
            unit: "ms",
            description: "Backend projection lag from outbox CreatedAtUtc to Query DB commit.");

        _batchSize = _meter.CreateHistogram<double>(
            "appointment_projection.batch.size",
            unit: "{event}",
            description: "Appointment projection processed events per batch.");

        _rebuildDurationMs = _meter.CreateHistogram<double>(
            "appointment_projection.rebuild.duration",
            unit: "ms",
            description: "Appointment projection rebuild duration.");

        _meter.CreateObservableGauge(
            "appointment_projection.pending",
            ObservePending,
            unit: "{message}",
            description: "Pending appointment projection outbox messages.");

        _meter.CreateObservableGauge(
            "appointment_projection.retry_waiting",
            ObserveRetryWaiting,
            unit: "{message}",
            description: "Retry-waiting appointment projection outbox messages.");

        _meter.CreateObservableGauge(
            "appointment_projection.dead_letter",
            ObserveDeadLetter,
            unit: "{message}",
            description: "Dead-letter appointment projection outbox messages.");

        _meter.CreateObservableGauge(
            "appointment_projection.oldest_pending_age",
            ObserveOldestPendingAge,
            unit: "s",
            description: "Oldest pending appointment projection message age.");

        _meter.CreateObservableGauge(
            "appointment_projection.enabled",
            ObserveProjectionEnabled,
            description: "Appointment projection enabled flag (1/0).");

        _meter.CreateObservableGauge(
            "appointment_projection.appointments_query_read_enabled",
            ObserveAppointmentsQueryReadEnabled,
            description: "Appointments query read flag (1/0).");

        _meter.CreateObservableGauge(
            "appointment_projection.dashboard_query_read_enabled",
            ObserveDashboardQueryReadEnabled,
            description: "Dashboard query read flag (1/0).");

        _meter.CreateObservableGauge(
            "appointment_projection.query_database_healthy",
            ObserveQueryDatabaseHealthy,
            description: "Query database reachable and migrated (1/0).");
    }

    public void RecordBatchCompleted(int processedCount, int failedCount, int deadLetteredCount, double durationMs)
    {
        var result = failedCount > 0 || deadLetteredCount > 0 ? "failed" : "success";
        _batchesTotal.Add(1, new KeyValuePair<string, object?>("result", result));

        if (processedCount > 0)
            _batchSize.Record(processedCount);

        if (durationMs >= 0)
            _batchDurationMs.Record(durationMs);
    }

    public void RecordEventProcessed(string? outboxType, double lagMs)
    {
        var eventType = AppointmentProjectionEventTypeTags.MapEventType(outboxType);
        var operation = AppointmentProjectionEventTypeTags.MapOperation(outboxType);

        _eventsProcessedTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", "success"));

        if (lagMs >= 0)
        {
            _eventLagMs.Record(lagMs,
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("operation", operation));
        }
    }

    public void RecordEventFailed(string? outboxType)
    {
        var eventType = AppointmentProjectionEventTypeTags.MapEventType(outboxType);
        var operation = AppointmentProjectionEventTypeTags.MapOperation(outboxType);

        _eventsFailedTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", "failed"));
    }

    public void RecordEventDeadLettered(string? outboxType)
    {
        var eventType = AppointmentProjectionEventTypeTags.MapEventType(outboxType);

        _eventsDeadLetteredTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("result", "dead_letter"));
    }

    public void RecordRebuildCompleted(double durationMs, bool success)
    {
        _rebuildsTotal.Add(1,
            new KeyValuePair<string, object?>("operation", "rebuild"),
            new KeyValuePair<string, object?>("result", success ? "success" : "failed"));

        if (durationMs >= 0)
            _rebuildDurationMs.Record(durationMs);
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<int>> ObservePending()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.PendingCount);
    }

    private IEnumerable<Measurement<int>> ObserveRetryWaiting()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.RetryWaitingCount);
    }

    private IEnumerable<Measurement<int>> ObserveDeadLetter()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.DeadLetterCount);
    }

    private IEnumerable<Measurement<double>> ObserveOldestPendingAge()
    {
        yield return new Measurement<double>(_snapshotHolder.Current.OldestPendingAgeSeconds);
    }

    private IEnumerable<Measurement<int>> ObserveProjectionEnabled()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.ProjectionEnabled);
    }

    private IEnumerable<Measurement<int>> ObserveAppointmentsQueryReadEnabled()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.AppointmentsQueryReadEnabled);
    }

    private IEnumerable<Measurement<int>> ObserveDashboardQueryReadEnabled()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.DashboardQueryReadEnabled);
    }

    private IEnumerable<Measurement<int>> ObserveQueryDatabaseHealthy()
    {
        yield return new Measurement<int>(_snapshotHolder.Current.QueryDatabaseHealthy);
    }
}
