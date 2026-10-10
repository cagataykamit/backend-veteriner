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

internal static class VeterinarianReaderMock
{
    /// <summary>Adı bilinen kullanıcılar için GetNamesAsync döner; diğerlerini sözlükte bırakmaz.</summary>
    public static Moq.Mock<Backend.Veteriner.Application.Clinics.Veterinarians.IClinicVeterinarianReader> Create(
        IReadOnlyDictionary<Guid, string?>? names = null)
    {
        var mock = new Moq.Mock<Backend.Veteriner.Application.Clinics.Veterinarians.IClinicVeterinarianReader>();
        mock.Setup(r => r.GetNamesAsync(Moq.It.IsAny<IReadOnlyCollection<Guid>>(), Moq.It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
            {
                var source = names ?? new Dictionary<Guid, string?>();
                IReadOnlyDictionary<Guid, string?> found = source
                    .Where(kv => ids.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                return Task.FromResult(found);
            });
        return mock;
    }
}
