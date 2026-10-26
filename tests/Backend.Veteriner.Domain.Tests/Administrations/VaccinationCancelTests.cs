using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Administrations;

public sealed class VaccinationCancelTests
{
    [Fact]
    public void CancelForVoidedAdministration_Should_Cancel_And_Clear_AppliedAt()
    {
        var applied = new Vaccination(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "Karma",
            VaccinationStatus.Applied, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(21), "not");

        applied.CancelForVoidedAdministration();

        applied.Status.Should().Be(VaccinationStatus.Cancelled);
        applied.AppliedAtUtc.Should().BeNull();
        applied.DueAtUtc.Should().NotBeNull();
        applied.UpdatedAtUtc.Should().NotBeNull();
    }
}
