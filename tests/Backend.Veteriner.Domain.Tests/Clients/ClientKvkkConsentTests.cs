using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clients;

/// <summary>ADR-021: KVKK açık rıza formu sonucu (Yok/Kabul/Ret), tarih ve işaretleyen kullanıcı; ADR-032: rıza metni sürümü.</summary>
public sealed class ClientKvkkConsentTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = new(2026, 10, 15);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Client CreateClient() => new(Guid.NewGuid(), "Ali Veli", Now, "905555555555");

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

        client.RecordKvkkConsent(status, null, Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();

        client.KvkkConsentStatus.Should().Be(status);
        client.KvkkConsentRecordedOn.Should().Be(Today);
        client.KvkkConsentRecordedByUserId.Should().Be(UserId);
        client.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Record_Should_Accept_Past_Date_And_Reject_Future_Date()
    {
        var client = CreateClient();

        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, Today.AddDays(-30), Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();
        client.KvkkConsentRecordedOn.Should().Be(Today.AddDays(-30));

        var future = client.RecordKvkkConsent(KvkkConsentStatus.Declined, Today.AddDays(1), Today, UserId, null, null, Now);
        future.IsSuccess.Should().BeFalse();
        future.Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.Accepted);
        client.KvkkConsentRecordedOn.Should().Be(Today.AddDays(-30));
    }

    [Fact]
    public void Revert_To_None_Should_Clear_Date_And_User_And_Reject_Date()
    {
        var client = CreateClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId, null, null, Now);

        client.RecordKvkkConsent(KvkkConsentStatus.None, Today, Today, UserId, null, null, Now).Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.Accepted);

        client.RecordKvkkConsent(KvkkConsentStatus.None, null, Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        client.KvkkConsentRecordedOn.Should().BeNull();
        client.KvkkConsentRecordedByUserId.Should().BeNull();
    }

    [Fact]
    public void Declined_Should_Opt_Out_Of_Reminders()
    {
        var client = CreateClient();

        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();

        client.RemindersOptedOut.Should().BeTrue();
        Client.AcceptsReminders.Compile()(client).Should().BeFalse();
    }

    [Theory]
    [InlineData(KvkkConsentStatus.Accepted)]
    [InlineData(KvkkConsentStatus.None)]
    public void Leaving_Declined_Should_Keep_Reminder_OptOut(KvkkConsentStatus next)
    {
        var client = CreateClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, Today, UserId, null, null, Now);

        client.RecordKvkkConsent(next, null, Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();

        client.RemindersOptedOut.Should().BeTrue();
    }

    [Fact]
    public void Accepted_Should_Not_Change_Reminder_OptOut()
    {
        var client = CreateClient();

        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId, null, null, Now);

        client.RemindersOptedOut.Should().BeFalse();
    }

    [Fact]
    public void Failed_Declined_Should_Not_Change_Reminder_OptOut()
    {
        var client = CreateClient();

        client.RecordKvkkConsent(KvkkConsentStatus.Declined, Today.AddDays(1), Today, UserId, null, null, Now).IsSuccess.Should().BeFalse();

        client.RemindersOptedOut.Should().BeFalse();
    }

    [Fact]
    public void New_Client_Should_Have_Unknown_Template_Version()
    {
        var client = CreateClient();

        client.KvkkConsentTemplateVersion.Should().BeNull();
        client.KvkkConsentTemplateHash.Should().BeNull();
    }

    [Theory]
    [InlineData(KvkkConsentStatus.Accepted, 0)]
    [InlineData(KvkkConsentStatus.Declined, 3)]
    public void Record_Should_Store_Template_Version_And_Hash(KvkkConsentStatus status, int version)
    {
        var client = CreateClient();
        var hash = ClinicKvkkFormTemplate.ComputeTextHash("Rıza metni");

        client.RecordKvkkConsent(status, null, Today, UserId, version, hash, Now).IsSuccess.Should().BeTrue();

        client.KvkkConsentTemplateVersion.Should().Be(version);
        client.KvkkConsentTemplateHash.Should().Be(hash);
    }

    [Fact]
    public void Record_Without_Template_Version_Should_Leave_Unknown()
    {
        var client = CreateClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId, 2, ClinicKvkkFormTemplate.ComputeTextHash("a"), Now);

        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();

        client.KvkkConsentTemplateVersion.Should().BeNull();
        client.KvkkConsentTemplateHash.Should().BeNull();
    }

    [Fact]
    public void Revert_To_None_Should_Clear_Template_Version()
    {
        var client = CreateClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId, 1, ClinicKvkkFormTemplate.ComputeTextHash("a"), Now);

        client.RecordKvkkConsent(KvkkConsentStatus.None, null, Today, UserId, null, null, Now).IsSuccess.Should().BeTrue();

        client.KvkkConsentTemplateVersion.Should().BeNull();
        client.KvkkConsentTemplateHash.Should().BeNull();
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, "valid")]
    [InlineData(-1, "valid")]
    [InlineData(1, "kısa")]
    public void Record_Should_Reject_Invalid_Template_Version(int? version, string? hash)
    {
        var client = CreateClient();
        var actualHash = hash == "valid" ? ClinicKvkkFormTemplate.ComputeTextHash("a") : hash;

        var result = client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, UserId, version, actualHash, Now);

        result.Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        client.KvkkConsentTemplateVersion.Should().BeNull();
    }

    [Fact]
    public void Record_Should_Reject_Invalid_Status_And_Empty_User()
    {
        var client = CreateClient();

        client.RecordKvkkConsent((KvkkConsentStatus)9, null, Today, UserId, null, null, Now).Error.Code.Should().Be("Clients.Validation");
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, Today, Guid.Empty, null, null, Now).Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
    }
}
