using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Imports.Commit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// B4 geri basınç (R4): eşikler sağlık eşiklerinin altında tetiklenir (işçi /health/ready 503 olmadan yavaşlar), kapalı
/// projeksiyon beklenmez, bekleme sırasında kira yenilenir, en uzun bekleme sonunda iş devam eder.
/// </summary>
public sealed class ImportBackpressureTests
{
    private static readonly ImportBackpressureOptions Defaults = new();

    private static ImportOutboxPressure Pressure(
        int outbox = 0, int clientPending = 0, double? clientAgeSeconds = null, bool petEnabled = false, int petPending = 0)
        => new(
            outbox,
            new OutboxHealthOptions().PendingDegradedThreshold,
            [
                new ImportProjectionPressure(
                    "client-projection", true, clientPending,
                    clientAgeSeconds is { } s ? TimeSpan.FromSeconds(s) : null,
                    new ClientProjectionHealthOptions().DegradedAfterSeconds),
                new ImportProjectionPressure("pet-projection", petEnabled, petPending, null, 10),
            ]);

    [Fact]
    public void Policy_Should_Wait_Before_Health_Thresholds_Degrade()
    {
        // Sağlık: genel outbox > 500 Degraded; müşteri projeksiyonu en eski olay ≥ 10 sn Degraded (ready = 503).
        ImportBackpressurePolicy.WaitReason(Pressure(outbox: 299), Defaults).Should().BeNull();
        ImportBackpressurePolicy.WaitReason(Pressure(outbox: 300), Defaults).Should().StartWith("outbox-pending 300 >= 300");
        ImportBackpressurePolicy.WaitReason(Pressure(clientPending: 3, clientAgeSeconds: 5.9), Defaults).Should().BeNull();
        ImportBackpressurePolicy.WaitReason(Pressure(clientPending: 3, clientAgeSeconds: 6), Defaults)
            .Should().StartWith("client-projection-oldest-age");
        ImportBackpressurePolicy.WaitReason(Pressure(clientPending: 200), Defaults).Should().StartWith("client-projection-pending 200");
    }

    [Fact]
    public void Policy_Should_Ignore_Disabled_Projection_And_Disabled_Backpressure()
    {
        ImportBackpressurePolicy.WaitReason(Pressure(petEnabled: false, petPending: 100_000), Defaults)
            .Should().BeNull("kapalı hasta projeksiyonunun olayları tasarım gereği birikir");
        ImportBackpressurePolicy.WaitReason(Pressure(outbox: 10_000), new ImportBackpressureOptions { Enabled = false })
            .Should().BeNull();
    }

    [Fact]
    public async Task Gate_Should_Wait_While_Pressured_Renewing_Lease_Then_Continue()
    {
        var reads = 0;
        var renewals = 0;
        var gate = new ImportBackpressureGate(
            new ImportBackpressureOptions { WaitSeconds = 0 }, TimeProvider.System, NullLogger.Instance);

        var proceed = await gate.WaitAsync(
            Guid.NewGuid(),
            _ => Task.FromResult(++reads <= 3 ? Pressure(outbox: 450) : Pressure()),
            _ => { renewals++; return Task.FromResult(true); },
            CancellationToken.None);

        proceed.Should().BeTrue();
        reads.Should().Be(4, "3 baskılı ölçüm + 1 temiz ölçüm");
        renewals.Should().Be(3, "her bekleme turunda kira yenilenir (çökme sonrası devralma ile karışmasın)");
    }

    [Fact]
    public async Task Gate_Should_Stop_When_Lease_Is_Lost_While_Waiting()
    {
        var gate = new ImportBackpressureGate(
            new ImportBackpressureOptions { WaitSeconds = 0 }, TimeProvider.System, NullLogger.Instance);

        var proceed = await gate.WaitAsync(
            Guid.NewGuid(), _ => Task.FromResult(Pressure(outbox: 450)), _ => Task.FromResult(false), CancellationToken.None);

        proceed.Should().BeFalse();
    }

    [Fact]
    public async Task Gate_Should_Continue_After_Max_Wait()
    {
        var reads = 0;
        var gate = new ImportBackpressureGate(
            new ImportBackpressureOptions { WaitSeconds = 0, MaxWaitSeconds = 0 }, TimeProvider.System, NullLogger.Instance);

        var proceed = await gate.WaitAsync(
            Guid.NewGuid(), _ => { reads++; return Task.FromResult(Pressure(outbox: 10_000)); }, _ => Task.FromResult(true), CancellationToken.None);

        proceed.Should().BeTrue("iş sonsuza kadar durmaz; uyarı günlüğe yazılır");
        reads.Should().Be(1);
    }
}
