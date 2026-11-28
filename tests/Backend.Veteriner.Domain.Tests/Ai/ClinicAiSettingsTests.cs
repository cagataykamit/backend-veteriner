using Backend.Veteriner.Domain.Ai;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Ai;

/// <summary>ADR-044 E1: klinik yöneticisi bilgilendirmeyi kabul ederek açar; kapatmak kabul istemez; sürüm artınca yeniden sorulur.</summary>
public sealed class ClinicAiSettingsTests
{
    private static readonly DateTime Now = new(2026, 11, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static ClinicAiSettings NewSettings() => ClinicAiSettings.Create(Guid.NewGuid(), Guid.NewGuid(), Now);

    [Fact]
    public void Enabling_Without_Accepting_The_Current_Version_Fails_And_Changes_Nothing()
    {
        var settings = NewSettings();

        settings.SetEnabledByClinic(true, 1, null, UserId, Now).Error.Should().Be(ClinicAiSettings.AcknowledgementRequired);
        settings.SetEnabledByClinic(true, 2, 1, UserId, Now).IsSuccess.Should().BeFalse("eski sürüm kabulü yetmez");

        settings.IsEnabled.Should().BeFalse();
        settings.AcknowledgedVersion.Should().BeNull();
        settings.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Accepting_Records_Who_And_When_Then_Later_Enables_Do_Not_Ask_Again()
    {
        var settings = NewSettings();

        settings.SetEnabledByClinic(true, 1, 1, UserId, Now).IsSuccess.Should().BeTrue();
        settings.IsEnabled.Should().BeTrue();
        (settings.AcknowledgedVersion, settings.AcknowledgedAtUtc, settings.AcknowledgedByUserId).Should().Be((1, Now, UserId));

        var other = Guid.NewGuid();
        settings.SetEnabledByClinic(false, 1, null, other, Now.AddHours(1)).IsSuccess.Should().BeTrue();
        settings.SetEnabledByClinic(true, 1, null, other, Now.AddHours(2)).IsSuccess.Should().BeTrue();
        settings.AcknowledgedByUserId.Should().Be(UserId, "kabul ilk kabul edenle kalır");
        settings.UpdatedByUserId.Should().Be(other);
    }

    [Fact]
    public void A_Newer_Version_Must_Be_Accepted_Again_But_Disabling_Never_Needs_It()
    {
        var settings = NewSettings();
        settings.SetEnabledByClinic(true, 1, 1, UserId, Now);

        settings.HasAcknowledged(2).Should().BeFalse();
        settings.SetEnabledByClinic(false, 2, null, UserId, Now).IsSuccess.Should().BeTrue();
        settings.SetEnabledByClinic(true, 2, null, UserId, Now).IsSuccess.Should().BeFalse();
        settings.SetEnabledByClinic(true, 2, 2, UserId, Now).IsSuccess.Should().BeTrue();
        settings.AcknowledgedVersion.Should().Be(2);
    }

    [Fact]
    public void Platform_Enable_Does_Not_Record_An_Acknowledgement()
    {
        var settings = NewSettings();

        settings.SetEnabled(true, null, Now);

        settings.IsEnabled.Should().BeTrue();
        settings.HasAcknowledged(1).Should().BeFalse();
    }
}
