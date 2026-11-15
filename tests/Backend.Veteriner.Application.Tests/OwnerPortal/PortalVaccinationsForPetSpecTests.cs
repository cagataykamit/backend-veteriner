using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>Kullanıcı kararı 2026-11-16: iptal edilmiş aşı portalda hiç görünmez; uygulanan ve planlı aşılar görünür.</summary>
public sealed class PortalVaccinationsForPetSpecTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    private Vaccination Vacc(VaccinationStatus status, Guid? petId = null)
        => new(_tenantId, petId ?? _petId, Guid.NewGuid(), null, Guid.NewGuid(), "Kuduz", status,
            status == VaccinationStatus.Applied ? Now.AddDays(-3) : null,
            status == VaccinationStatus.Applied ? null : Now.AddDays(5), null, Now);

    [Fact]
    public void Should_Drop_Cancelled_And_Keep_Applied_And_Scheduled()
    {
        var applied = Vacc(VaccinationStatus.Applied);
        var scheduled = Vacc(VaccinationStatus.Scheduled);
        var cancelled = Vacc(VaccinationStatus.Scheduled);
        cancelled.Cancel(Guid.NewGuid(), "Yanlış kayıt", Now).IsSuccess.Should().BeTrue();
        var otherPet = Vacc(VaccinationStatus.Applied, Guid.NewGuid());

        var rows = new PortalVaccinationsForPetSpec(_tenantId, _petId)
            .Evaluate([applied, scheduled, cancelled, otherPet])
            .ToArray();

        rows.Select(r => r.Id).Should().BeEquivalentTo([applied.Id, scheduled.Id]);
        rows.Should().NotContain(r => r.Status == VaccinationStatus.Cancelled);
    }
}
