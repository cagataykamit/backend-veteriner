using Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings;
using Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Clinics.AppointmentSettings;

public sealed class UpdateClinicAppointmentSettingsCommandValidatorTests
{
    private readonly UpdateClinicAppointmentSettingsCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_DurationOutOfRange()
    {
        _validator.Validate(new UpdateClinicAppointmentSettingsCommand(Guid.NewGuid(), 4, 15, false))
            .IsValid.Should().BeFalse();
        _validator.Validate(new UpdateClinicAppointmentSettingsCommand(Guid.NewGuid(), 241, 15, false))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_SlotIntervalOutOfRange()
    {
        _validator.Validate(new UpdateClinicAppointmentSettingsCommand(Guid.NewGuid(), 30, 4, false))
            .IsValid.Should().BeFalse();
        _validator.Validate(new UpdateClinicAppointmentSettingsCommand(Guid.NewGuid(), 30, 121, false))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_Should_Pass_When_Valid()
    {
        _validator.Validate(new UpdateClinicAppointmentSettingsCommand(Guid.NewGuid(), 30, 15, false))
            .IsValid.Should().BeTrue();
    }
}
