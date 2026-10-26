using Backend.Veteriner.Domain.Clients;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

public sealed class ClientContactPreferencesTests
{
    private static Client CreateClient() => new(Guid.NewGuid(), "Ali Veli", "905555555555", "ali@example.com");

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
}
