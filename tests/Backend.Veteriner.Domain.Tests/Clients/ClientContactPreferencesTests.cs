using Backend.Veteriner.Domain.Clients;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

public sealed class ClientContactPreferencesTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static Client CreateClient() => new(Guid.NewGuid(), "Ali Veli", Now, "905555555555", "ali@example.com");

    [Fact]
    public void New_Client_Should_Have_No_Preference_And_Accept_Reminders()
    {
        var client = CreateClient();

        client.PreferredContactChannel.Should().Be(ContactChannel.None);
        client.RemindersOptedOut.Should().BeFalse();
        Client.AcceptsReminders.Compile()(client).Should().BeTrue();
    }

    [Fact]
    public void ApplyContactPreferences_Should_Set_Values()
    {
        var client = CreateClient();

        client.ApplyContactPreferences(ContactChannel.WhatsApp, true).IsSuccess.Should().BeTrue();

        client.PreferredContactChannel.Should().Be(ContactChannel.WhatsApp);
        client.RemindersOptedOut.Should().BeTrue();
        Client.AcceptsReminders.Compile()(client).Should().BeFalse();
    }

    [Fact]
    public void ApplyContactPreferences_Null_Should_Not_Touch_Existing_Values()
    {
        var client = CreateClient();
        client.ApplyContactPreferences(ContactChannel.Sms, true);

        client.ApplyContactPreferences(null, null).IsSuccess.Should().BeTrue();

        client.PreferredContactChannel.Should().Be(ContactChannel.Sms);
        client.RemindersOptedOut.Should().BeTrue();
    }

    [Fact]
    public void ApplyContactPreferences_None_Should_Clear_Channel()
    {
        var client = CreateClient();
        client.ApplyContactPreferences(ContactChannel.Phone, null);

        client.ApplyContactPreferences(ContactChannel.None, false).IsSuccess.Should().BeTrue();

        client.PreferredContactChannel.Should().Be(ContactChannel.None);
        client.RemindersOptedOut.Should().BeFalse();
    }

    [Fact]
    public void ApplyContactPreferences_Undefined_Channel_Should_Fail_Without_Change()
    {
        var client = CreateClient();

        var result = client.ApplyContactPreferences((ContactChannel)99, true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.Validation");
        client.PreferredContactChannel.Should().Be(ContactChannel.None);
        client.RemindersOptedOut.Should().BeFalse();
    }

    [Fact]
    public void Owner_Can_Opt_Out_And_Back_In_Through_The_Same_Flag()
    {
        var client = CreateClient();
        var later = Now.AddHours(1);

        client.SetRemindersOptedOutByOwner(true, later).IsSuccess.Should().BeTrue();
        client.RemindersOptedOut.Should().BeTrue();
        Client.AcceptsReminders.Compile()(client).Should().BeFalse();
        client.UpdatedAtUtc.Should().Be(later);

        client.SetRemindersOptedOutByOwner(false, later.AddHours(1)).IsSuccess.Should().BeTrue();
        client.RemindersOptedOut.Should().BeFalse();
        Client.AcceptsReminders.Compile()(client).Should().BeTrue();
    }

    [Fact]
    public void Owner_Setting_Same_Value_Should_Not_Touch_Record()
    {
        var client = CreateClient();

        client.SetRemindersOptedOutByOwner(false, Now.AddHours(1)).IsSuccess.Should().BeTrue();

        client.UpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Owner_Cannot_Enable_Reminders_While_Kvkk_Is_Declined_But_Can_Keep_Them_Off()
    {
        var client = CreateClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, DateOnly.FromDateTime(Now), Guid.NewGuid(), null, null, Now).IsSuccess.Should().BeTrue();

        client.OwnerCanEnableReminders.Should().BeFalse();
        var enable = client.SetRemindersOptedOutByOwner(false, Now.AddHours(1));

        enable.IsSuccess.Should().BeFalse();
        enable.Error.Code.Should().Be("Clients.RemindersKvkkDeclined");
        client.RemindersOptedOut.Should().BeTrue();
        client.SetRemindersOptedOutByOwner(true, Now.AddHours(1)).IsSuccess.Should().BeTrue();
    }
}
