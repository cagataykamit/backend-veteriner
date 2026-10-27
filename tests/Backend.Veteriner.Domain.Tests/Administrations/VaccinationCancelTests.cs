using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Administrations;

public sealed class VaccinationCancelTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CancelForVoidedAdministration_Should_Cancel_And_Clear_AppliedAt()
    {
        var applied = new Vaccination(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Karma",
            VaccinationStatus.Applied, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(21), "not", Now);

        applied.CancelForVoidedAdministration(Now);

        applied.Status.Should().Be(VaccinationStatus.Cancelled);
        applied.AppliedAtUtc.Should().BeNull();
        applied.DueAtUtc.Should().NotBeNull();
        applied.UpdatedAtUtc.Should().NotBeNull();
    }
}
