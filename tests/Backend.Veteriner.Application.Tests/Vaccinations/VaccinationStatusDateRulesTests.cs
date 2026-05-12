using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Vaccinations;

public sealed class VaccinationStatusDateRulesTests
{
    private static readonly DateTime Ref = new(2026, 5, 10, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Scheduled_WithAppliedAt_Should_Fail()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Scheduled,
            Ref.AddDays(-1),
            Ref.AddDays(7),
            Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.ScheduledMustNotHaveAppliedAt");
    }

    [Fact]
    public void Scheduled_WithoutDue_Should_Fail()
    {
        var r = VaccinationStatusDateRules.Validate(VaccinationStatus.Scheduled, null, null, Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.ScheduledRequiresDueAt");
    }

    [Fact]
    public void Scheduled_WithDueEarlierSameDay_Should_Fail()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Scheduled,
            null,
            new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
            Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.ScheduledDueAtMustNotBePast");
    }

    [Fact]
    public void Scheduled_WithDueEqualToReference_Should_Fail()
    {
        var r = VaccinationStatusDateRules.Validate(VaccinationStatus.Scheduled, null, Ref, Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.ScheduledDueAtMustNotBePast");
    }

    [Fact]
    public void Scheduled_WithDueOneMinuteAfterReference_Should_Succeed()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Scheduled,
            null,
            Ref.AddMinutes(1),
            Ref);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Scheduled_WithDueNextDay_Should_Succeed()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Scheduled,
            null,
            new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Utc),
            Ref);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Applied_WithoutAppliedAt_Should_Fail()
    {
        var r = VaccinationStatusDateRules.Validate(VaccinationStatus.Applied, null, Ref.AddDays(10), Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.AppliedRequiresAppliedAt");
    }

    [Fact]
    public void Applied_WithFutureAppliedAt_Should_Fail()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Applied,
            Ref.AddHours(1),
            null,
            Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.AppliedAtMustNotBeFuture");
    }

    [Fact]
    public void Applied_WithAppliedAtEqualToReference_Should_Succeed()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Applied,
            Ref,
            null,
            Ref);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Applied_WithNullDue_Should_Succeed()
    {
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Applied,
            Ref.AddDays(-1),
            null,
            Ref);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Applied_WithDueAfterApplied_Should_Succeed()
    {
        var applied = Ref.AddDays(-2);
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Applied,
            applied,
            applied.AddDays(1),
            Ref);
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Applied_WithDueEqualToApplied_Should_Fail()
    {
        var applied = Ref.AddDays(-2);
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Applied,
            applied,
            applied,
            Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.DueAtMustBeAfterAppliedAt");
    }

    [Fact]
    public void Applied_WithDueBeforeApplied_Should_Fail()
    {
        var applied = Ref.AddDays(-2);
        var r = VaccinationStatusDateRules.Validate(
            VaccinationStatus.Applied,
            applied,
            applied.AddDays(-1),
            Ref);
        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Vaccinations.DueAtMustBeAfterAppliedAt");
    }
}
