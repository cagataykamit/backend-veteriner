namespace Backend.IntegrationTests.Infrastructure;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTime utcNow)
        => _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTime utcNow)
        => _utcNow = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
