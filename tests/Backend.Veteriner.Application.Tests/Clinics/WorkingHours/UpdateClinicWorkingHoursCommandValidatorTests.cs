using Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;
using Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours.Validators;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Clinics.WorkingHours;

public sealed class UpdateClinicWorkingHoursCommandValidatorTests
{
    private readonly UpdateClinicWorkingHoursCommandValidator _validator = new();

    [Fact]
    public void Should_Fail_When_NotSevenItems()
    {
        var week = ClinicWorkingHoursDefaults.BuildWeek().Take(6).ToList();
        var r = _validator.Validate(new UpdateClinicWorkingHoursCommand(Guid.NewGuid(), week));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_DuplicateDay()
    {
        var baseWeek = ClinicWorkingHoursDefaults.BuildWeek().ToList();
        baseWeek[1] = baseWeek[0];
        var r = _validator.Validate(new UpdateClinicWorkingHoursCommand(Guid.NewGuid(), baseWeek));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_OpenDay_MissingHours()
    {
        var items = Enum.GetValues<DayOfWeek>()
            .Select(d => new ClinicWorkingHourDto(d, false, null, null, null, null))
            .ToList();
        var r = _validator.Validate(new UpdateClinicWorkingHoursCommand(Guid.NewGuid(), items));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_BreakOutsideHours()
    {
        var items = Enum.GetValues<DayOfWeek>()
            .Select(d =>
                d == DayOfWeek.Sunday
                    ? new ClinicWorkingHourDto(d, true, null, null, null, null)
                    : new ClinicWorkingHourDto(
                        d,
                        false,
                        new TimeOnly(9, 0),
                        new TimeOnly(18, 0),
                        new TimeOnly(8, 0),
                        new TimeOnly(8, 30)))
            .ToList();
        var r = _validator.Validate(new UpdateClinicWorkingHoursCommand(Guid.NewGuid(), items));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Pass_When_ValidWeek()
    {
        var r = _validator.Validate(
            new UpdateClinicWorkingHoursCommand(Guid.NewGuid(), ClinicWorkingHoursDefaults.BuildWeek()));
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_ClosedDay_HasHours()
    {
        var items = Enum.GetValues<DayOfWeek>()
            .Select(d =>
                d == DayOfWeek.Sunday
                    ? new ClinicWorkingHourDto(d, true, new TimeOnly(9, 0), new TimeOnly(10, 0), null, null)
                    : new ClinicWorkingHourDto(d, false, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null))
            .ToList();
        var r = _validator.Validate(new UpdateClinicWorkingHoursCommand(Guid.NewGuid(), items));
        r.IsValid.Should().BeFalse();
    }
}
