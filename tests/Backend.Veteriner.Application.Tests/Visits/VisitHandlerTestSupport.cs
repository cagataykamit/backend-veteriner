using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Tests.Visits;

internal static class VisitHandlerTestSupport
{
    public static readonly DateTime FixedNowUtc = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    public static TimeProvider FixedClock { get; } = new FixedTimeProvider(FixedNowUtc);

    public static Visit NewVisit(Guid tenantId, Guid clinicId, Guid? petId = null, Guid? appointmentId = null)
        => new(tenantId, clinicId, petId ?? Guid.NewGuid(), appointmentId, null, Guid.NewGuid(), FixedNowUtc);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
