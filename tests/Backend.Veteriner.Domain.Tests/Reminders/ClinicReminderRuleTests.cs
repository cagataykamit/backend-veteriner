using Backend.Veteriner.Domain.Reminders;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Reminders;

/// <summary>TIER-003: kural varsayılan kapalı, gün sınırı, açılış anı ve ticari ileti sınıflandırması.</summary>
public sealed class ClinicReminderRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static ClinicReminderRule New(ClinicReminderRuleType type = ClinicReminderRuleType.Debt)
        => ClinicReminderRule.CreateDisabled(Guid.NewGuid(), Guid.NewGuid(), type, Now);

    [Theory]
    [InlineData(ClinicReminderRuleType.FollowUp, 3)]
    [InlineData(ClinicReminderRuleType.Debt, 30)]
    [InlineData(ClinicReminderRuleType.NoReturn, 180)]
    public void CreateDisabled_Should_Be_Off_With_Default_Days(ClinicReminderRuleType type, int expectedDays)
    {
        var rule = New(type);

        rule.IsEnabled.Should().BeFalse();
        rule.Days.Should().Be(expectedDays);
        rule.EnabledAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(ClinicReminderRuleType.FollowUp, 0)]
    [InlineData(ClinicReminderRuleType.FollowUp, 31)]
    [InlineData(ClinicReminderRuleType.Debt, 366)]
    [InlineData(ClinicReminderRuleType.NoReturn, 29)]
    [InlineData(ClinicReminderRuleType.NoReturn, 731)]
    public void Update_Should_Reject_Days_Out_Of_Range_And_Change_Nothing(ClinicReminderRuleType type, int days)
    {
        var rule = New(type);

        var result = rule.Update(true, days, UserId, Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(ClinicReminderRule.DaysOutOfRangeCode);
        rule.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnabledAt_Should_Change_Only_On_Off_To_On_Transition()
    {
        var rule = New();

        rule.Update(true, 30, UserId, Now).IsSuccess.Should().BeTrue();
        rule.EnabledAtUtc.Should().Be(Now);

        rule.Update(true, 45, UserId, Now.AddDays(1)).IsSuccess.Should().BeTrue();
        rule.EnabledAtUtc.Should().Be(Now, "gün değiştirmek geçmişi yeniden açmaz");

        rule.Update(false, 45, UserId, Now.AddDays(2)).IsSuccess.Should().BeTrue();
        rule.EnabledAtUtc.Should().Be(Now);

        rule.Update(true, 45, UserId, Now.AddDays(3)).IsSuccess.Should().BeTrue();
        rule.EnabledAtUtc.Should().Be(Now.AddDays(3));
    }

    [Theory]
    [InlineData(ClinicReminderRuleType.FollowUp, true)]
    [InlineData(ClinicReminderRuleType.Debt, true)]
    [InlineData(ClinicReminderRuleType.NoReturn, false)]
    public void Only_NoReturn_Should_Be_Commercial(ClinicReminderRuleType type, bool expectedReminder)
        => ClinicReminderRuleDefinition.Of(type).IsMarketing.Should().Be(!expectedReminder);
}
