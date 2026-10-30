using Backend.Veteriner.Domain.Appointments;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Appointments;

public sealed class AppointmentReasonTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Should_Normalize_Name_Dedupe_Veterinarians_And_Start_Active()
    {
        var vet = Guid.NewGuid();
        var reason = AppointmentReason.Create(TenantId, ClinicId, "  Aşı ", 15, true, 1, [vet, vet], Now);

        reason.Name.Should().Be("Aşı");
        reason.IsActive.Should().BeTrue();
        reason.IsAvailableOnline.Should().BeTrue();
        reason.Veterinarians.Select(v => v.UserId).Should().Equal(vet);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(241)]
    public void Create_Should_Reject_Duration_Outside_Appointment_Range(int minutes)
    {
        var act = () => AppointmentReason.Create(TenantId, ClinicId, "Aşı", minutes, true, 0, [], Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Empty_Veterinarian_List_Should_Allow_Every_Veterinarian()
    {
        var vet = Guid.NewGuid();
        var open = AppointmentReason.Create(TenantId, ClinicId, "Kontrol", 20, true, 0, [], Now);
        var restricted = AppointmentReason.Create(TenantId, ClinicId, "Cerrahi", 60, false, 0, [vet], Now);

        open.AllowsVeterinarian(Guid.NewGuid()).Should().BeTrue();
        restricted.AllowsVeterinarian(vet).Should().BeTrue();
        restricted.AllowsVeterinarian(Guid.NewGuid()).Should().BeFalse();
        restricted.IsAvailableOnline.Should().BeFalse();
    }

    [Fact]
    public void Update_Should_Replace_Veterinarians_And_SetActive_Should_Be_Idempotent()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var reason = AppointmentReason.Create(TenantId, ClinicId, "Aşı", 15, true, 0, [a], Now);

        reason.Update("Aşı", 15, true, 0, [b], Now.AddMinutes(1));
        reason.Veterinarians.Select(v => v.UserId).Should().Equal(b);

        reason.SetActive(false, Now).Should().BeTrue();
        reason.SetActive(false, Now).Should().BeFalse();
        reason.IsAvailableOnline.Should().BeFalse();
    }

    [Fact]
    public void Appointment_Should_Carry_Reason_And_Veterinarian_And_Reject_Empty_Ids()
    {
        var vet = Guid.NewGuid();
        var reasonId = Guid.NewGuid();
        var appointment = new Appointment(TenantId, ClinicId, Guid.NewGuid(), Now.AddDays(1), 30, reasonId: reasonId, veterinarianUserId: vet);

        appointment.ReasonId.Should().Be(reasonId);
        appointment.VeterinarianUserId.Should().Be(vet);

        var act = () => new Appointment(TenantId, ClinicId, Guid.NewGuid(), Now.AddDays(1), veterinarianUserId: Guid.Empty);
        act.Should().Throw<ArgumentException>();

        appointment.UpdateDetails(ClinicId, appointment.PetId, Now.AddDays(2), 30, AppointmentType.Other, null, null, null)
            .IsSuccess.Should().BeTrue();
        appointment.VeterinarianUserId.Should().BeNull();
    }
}
