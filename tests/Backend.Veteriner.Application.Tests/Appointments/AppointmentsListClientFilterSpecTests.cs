using Ardalis.Specification;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Domain.Appointments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Appointments;

public sealed class AppointmentsListClientFilterSpecTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private Appointment NewAppointment(Guid petId, Guid? tenantId = null)
        => new(tenantId ?? _tenantId, _clinicId, petId, DateTime.UtcNow.AddDays(1));

    [Fact]
    public void ClientPetIds_Should_Filter_Count_And_Page_Within_Tenant()
    {
        var petA = Guid.NewGuid();
        var mine = NewAppointment(petA);
        var otherClient = NewAppointment(Guid.NewGuid());
        var otherTenantSamePet = NewAppointment(petA, Guid.NewGuid());
        var all = new[] { mine, otherClient, otherTenantSamePet };

        new AppointmentsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, [], clientPetIds: [petA])
            .Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        new AppointmentsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, null, 1, 20, null, [], true, clientPetIds: [petA])
            .Evaluate(all).Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public void Without_ClientPetIds_Should_Not_Filter_By_Client()
    {
        var all = new[] { NewAppointment(Guid.NewGuid()), NewAppointment(Guid.NewGuid()) };

        new AppointmentsFilteredCountSpec(_tenantId, _clinicId, null, null, null, null, null, [])
            .Evaluate(all).Should().HaveCount(2);
    }
}
