using Backend.Veteriner.Domain.Clients;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

/// <summary>ADR-021: KVKK açık rıza formu sonucu (Yok/Kabul/Ret), tarih ve işaretleyen kullanıcı.</summary>
public sealed class ClientKvkkConsentTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Client CreateClient() => new(Guid.NewGuid(), "Ali Veli", "905555555555");

    [Fact]
    public void New_Client_Should_Have_No_Consent()
    {
        var client = CreateClient();

        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        client.KvkkConsentRecordedOn.Should().BeNull();
        client.KvkkConsentRecordedByUserId.Should().BeNull();
    }

    [Theory]
    [InlineData(KvkkConsentStatus.Accepted)]
    [InlineData(KvkkConsentStatus.Declined)]
    public void Record_Should_Default_Date_To_Today_And_Set_User(KvkkConsentStatus status)
    {
        var client = CreateClient();

        client.RecordKvkkConsent(status, null, Today, UserId).IsSuccess.Should().BeTrue();

        client.KvkkConsentStatus.Should().Be(status);
        client.KvkkConsentRecordedOn.Should().Be(Today);
        client.KvkkConsentRecordedByUserId.Should().Be(UserId);
        client.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Record_Should_Accept_Past_Date_And_Reject_Future_Date()
    {
        var client = CreateClient();

        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, Today.AddDays(-30), Today, UserId).IsSuccess.Should().BeTrue();
        client.KvkkConsentRecordedOn.Should().Be(Today.AddDays(-30));

        var future = client.RecordKvkkConsent(KvkkConsentStatus.Declined, Today.AddDays(1), Today, UserId);
        future.IsSuccess.Should().BeFalse();
        future.Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.Accepted);
        client.KvkkConsentRecordedOn.Should().Be(Today.AddDays(-30));
    }

    [Fact]
    public void Revert_To_None_Should_Clear_Date_And_User_And_Reject_Date()
    {
        var client = CreateClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId);

        client.RecordKvkkConsent(KvkkConsentStatus.None, Today, Today, UserId).Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.Accepted);

        client.RecordKvkkConsent(KvkkConsentStatus.None, null, Today, UserId).IsSuccess.Should().BeTrue();
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        client.KvkkConsentRecordedOn.Should().BeNull();
        client.KvkkConsentRecordedByUserId.Should().BeNull();
    }

    [Fact]
    public void Declined_Should_Not_Change_Reminder_OptOut()
    {
        var client = CreateClient();

        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, Today, UserId);

        client.RemindersOptedOut.Should().BeFalse();
        Client.AcceptsReminders.Compile()(client).Should().BeTrue();
    }

    [Fact]
    public void Record_Should_Reject_Invalid_Status_And_Empty_User()
    {
        var client = CreateClient();

        client.RecordKvkkConsent((KvkkConsentStatus)9, null, Today, UserId).Error.Code.Should().Be("Clients.Validation");
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, Guid.Empty).Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
    }
}
