using System.Diagnostics.Metrics;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using FluentAssertions;

namespace Backend.IntegrationTests.Projections.Appointments;

[CollectionDefinition("appointment-projection-metrics", DisableParallelization = true)]
public sealed class AppointmentProjectionMetricsCollection;

[Collection("appointment-projection-metrics")]
public sealed class AppointmentProjectionMetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly AppointmentProjectionMetricsSnapshotHolder _holder = new();
    private readonly AppointmentProjectionMetrics _metrics;
    private readonly Dictionary<string, List<Measurement<long>>> _longMeasurements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _observableInts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _observableDoubles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Measurement<double>>> _doubleMeasurements = new(StringComparer.Ordinal);

    public AppointmentProjectionMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name != AppointmentProjectionMetrics.MeterName)
                return;

            listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            RecordLong(instrument.Name, measurement, tags));

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name is "appointment_projection.batch.duration"
                or "appointment_projection.event.lag"
                or "appointment_projection.batch.size"
                or "appointment_projection.rebuild.duration")
            {
                RecordDouble(instrument.Name, measurement, tags);
            }
            else
            {
                _observableDoubles[instrument.Name] = measurement;
            }
        });

        _listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
            _observableInts[instrument.Name] = measurement);

        _listener.Start();
        _metrics = new AppointmentProjectionMetrics(_holder);
    }

    [Fact]
    public void RecordBatchCompleted_Should_IncrementBatchesCounter_OnSuccess()
    {
        _metrics.RecordBatchCompleted(processedCount: 3, failedCount: 0, deadLetteredCount: 0, durationMs: 12.5);

        SumLong("appointment_projection.batches.total").Should().Be(1);
        TagValue("appointment_projection.batches.total", "result").Should().Be("success");
    }

    [Fact]
    public void RecordBatchCompleted_Should_IncrementProcessedEventsCounter()
    {
        _metrics.RecordEventProcessed(AppointmentIntegrationEventTypes.Created, lagMs: 5);
        _metrics.RecordEventProcessed(AppointmentIntegrationEventTypes.Rescheduled, lagMs: 7);
        _metrics.RecordEventProcessed(AppointmentIntegrationEventTypes.Cancelled, lagMs: 9);

        SumLong("appointment_projection.events.processed.total").Should().Be(3);
    }

    [Fact]
    public void RecordEventFailed_Should_IncrementFailedCounter()
    {
        _metrics.RecordEventFailed(AppointmentIntegrationEventTypes.Created);

        SumLong("appointment_projection.events.failed.total").Should().Be(1);
        TagValue("appointment_projection.events.failed.total", "result").Should().Be("failed");
    }

    [Fact]
    public void RecordEventDeadLettered_Should_IncrementDeadLetterCounter()
    {
        _metrics.RecordEventDeadLettered(AppointmentIntegrationEventTypes.Cancelled);

        SumLong("appointment_projection.events.dead_lettered.total").Should().Be(1);
        TagValue("appointment_projection.events.dead_lettered.total", "result").Should().Be("dead_letter");
    }

    [Fact]
    public void RecordBatchCompleted_Should_RecordBatchDurationHistogram()
    {
        _metrics.RecordBatchCompleted(1, 0, 0, durationMs: 42.2);

        MaxDouble("appointment_projection.batch.duration").Should().BeApproximately(42.2, 0.01);
    }

    [Fact]
    public void RecordBatchCompleted_Should_RecordBatchSizeHistogram()
    {
        _metrics.RecordBatchCompleted(processedCount: 7, failedCount: 0, deadLetteredCount: 0, durationMs: 1);

        MaxDouble("appointment_projection.batch.size").Should().Be(7);
    }

    [Fact]
    public void RecordEventProcessed_Should_NotRecordNegativeLag()
    {
        _metrics.RecordEventProcessed(AppointmentIntegrationEventTypes.Created, lagMs: -5);

        HasDouble("appointment_projection.event.lag").Should().BeFalse();
    }

    [Fact]
    public void RecordEventProcessed_Should_UseUnknownTag_ForUnknownEventType()
    {
        _metrics.RecordEventProcessed("Some.Unknown.Event", lagMs: 10);

        TagValue("appointment_projection.events.processed.total", "event_type").Should().Be(AppointmentProjectionEventTypeTags.Unknown);
        TagValue("appointment_projection.events.processed.total", "operation").Should().Be(AppointmentProjectionEventTypeTags.Unknown);
    }

    [Fact]
    public void RecordEventProcessed_Should_UseAllowlistedTags_ForKnownEventType()
    {
        _metrics.RecordEventProcessed(AppointmentIntegrationEventTypes.Rescheduled, lagMs: 10);

        TagValue("appointment_projection.events.processed.total", "event_type")
            .Should().Be(AppointmentProjectionEventTypeTags.EventRescheduled);
        TagValue("appointment_projection.events.processed.total", "operation")
            .Should().Be(AppointmentProjectionEventTypeTags.OperationReschedule);
    }

    [Fact]
    public void RecordEventProcessed_Should_NotIncludeHighCardinalityTags()
    {
        _metrics.RecordEventProcessed(AppointmentIntegrationEventTypes.Created, lagMs: 10);

        var tags = AllTagKeys("appointment_projection.events.processed.total");
        tags.Should().NotContain("tenant_id");
        tags.Should().NotContain("clinic_id");
        tags.Should().NotContain("user_id");
        tags.Should().NotContain("appointment_id");
        tags.Should().NotContain("TenantId");
        tags.Should().NotContain("ClinicId");
    }

    [Fact]
    public void RecordRebuildCompleted_Should_RecordSuccessAndFailureCounters()
    {
        _metrics.RecordRebuildCompleted(durationMs: 100, success: true);
        _metrics.RecordRebuildCompleted(durationMs: 50, success: false);

        SumLong("appointment_projection.rebuilds.total").Should().Be(2);
        TagValues("appointment_projection.rebuilds.total", "result")
            .Should().BeEquivalentTo(["success", "failed"]);
    }

    [Fact]
    public void ObservableGauges_Should_ReadFromSnapshotHolder()
    {
        _holder.Update(new AppointmentProjectionMetricsSnapshot(
            PendingCount: 4,
            RetryWaitingCount: 2,
            DeadLetterCount: 1,
            OldestPendingAgeSeconds: 15.5,
            ProjectionEnabled: 1,
            AppointmentsQueryReadEnabled: 1,
            DashboardQueryReadEnabled: 0,
            QueryDatabaseHealthy: 1,
            Mode: "full-query"));

        _listener.RecordObservableInstruments();

        ObservableInt("appointment_projection.pending").Should().Be(4);
        ObservableInt("appointment_projection.retry_waiting").Should().Be(2);
        ObservableInt("appointment_projection.dead_letter").Should().Be(1);
        ObservableDouble("appointment_projection.oldest_pending_age").Should().BeApproximately(15.5, 0.01);
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _listener.Dispose();
    }

    private void RecordLong(string name, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!_longMeasurements.TryGetValue(name, out var list))
        {
            list = [];
            _longMeasurements[name] = list;
        }

        list.Add(new Measurement<long>(value, tags.ToArray()));
    }

    private void RecordDouble(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (!_doubleMeasurements.TryGetValue(name, out var list))
        {
            list = [];
            _doubleMeasurements[name] = list;
        }

        list.Add(new Measurement<double>(value, tags.ToArray()));
    }

    private long SumLong(string name)
        => _longMeasurements.TryGetValue(name, out var list)
            ? list.Sum(m => m.Value)
            : 0;

    private double MaxDouble(string name)
        => _doubleMeasurements.TryGetValue(name, out var list) && list.Count > 0
            ? list.Max(m => m.Value)
            : 0;

    private bool HasDouble(string name)
        => _doubleMeasurements.ContainsKey(name) && _doubleMeasurements[name].Count > 0;

    private string? TagValue(string instrumentName, string tagKey)
    {
        if (!_longMeasurements.TryGetValue(instrumentName, out var list) || list.Count == 0)
            return null;

        foreach (var tag in list[^1].Tags)
        {
            if (tag.Key == tagKey)
                return tag.Value?.ToString();
        }

        return null;
    }

    private IReadOnlyList<string> TagValues(string instrumentName, string tagKey)
    {
        if (!_longMeasurements.TryGetValue(instrumentName, out var list))
            return [];

        var values = new List<string>();
        foreach (var measurement in list)
        {
            foreach (var tag in measurement.Tags)
            {
                if (tag.Key == tagKey)
                    values.Add(tag.Value?.ToString() ?? string.Empty);
            }
        }

        return values;
    }

    private IReadOnlyList<string> AllTagKeys(string instrumentName)
    {
        if (!_longMeasurements.TryGetValue(instrumentName, out var list) || list.Count == 0)
            return [];

        var keys = new List<string>();
        foreach (var tag in list[^1].Tags)
            keys.Add(tag.Key);

        return keys;
    }

    private int ObservableInt(string name)
        => _observableInts.TryGetValue(name, out var value) ? value : 0;

    private double ObservableDouble(string name)
        => _observableDoubles.TryGetValue(name, out var value) ? value : 0;
}

[Collection("appointment-projection-metrics")]
public sealed class AppointmentProjectionMonitoringOptionsValidatorTests
{
    private readonly AppointmentProjectionMonitoringOptionsValidator _validator = new();

    [Fact]
    public void Validate_Should_FailFast_WhenWarningPendingAgeIsInvalid()
    {
        var result = _validator.Validate(null, new AppointmentProjectionMonitoringOptions
        {
            WarningPendingAgeSeconds = 0,
            CriticalPendingAgeSeconds = 30
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("WarningPendingAgeSeconds");
    }

    [Fact]
    public void Validate_Should_FailFast_WhenCriticalPendingAgeIsNotGreaterThanWarning()
    {
        var result = _validator.Validate(null, new AppointmentProjectionMonitoringOptions
        {
            WarningPendingAgeSeconds = 30,
            CriticalPendingAgeSeconds = 10
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("CriticalPendingAgeSeconds");
    }

    [Fact]
    public void Validate_Should_FailFast_WhenParityIntervalIsInvalid()
    {
        var result = _validator.Validate(null, new AppointmentProjectionMonitoringOptions
        {
            ParityCheckIntervalSeconds = 0
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ParityCheckIntervalSeconds");
    }
}
