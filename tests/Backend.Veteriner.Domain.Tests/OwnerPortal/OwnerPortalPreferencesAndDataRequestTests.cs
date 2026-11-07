using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.OwnerPortal;

/// <summary>PORTAL-007/009/010: bildirim tercihleri, veri talebi yaşam döngüsü, klinik portal ayarı, tür × kanal kuralı.</summary>
public sealed class OwnerPortalPreferencesAndDataRequestTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static OwnerAccountClientLink NewLink()
        => OwnerAccountClientLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1));

    [Fact]
    public void Marketing_Consent_Records_Time_And_Withdrawal_Clears_It()
    {
        var link = NewLink();

        link.SetMarketingConsent(true, Now).Should().BeTrue();
        link.MarketingConsentAtUtc.Should().Be(Now);
        link.SetMarketingConsent(true, Now.AddHours(1)).Should().BeFalse();
        link.MarketingConsentAtUtc.Should().Be(Now);

        link.SetMarketingConsent(false, Now).Should().BeTrue();
        link.MarketingConsentAtUtc.Should().BeNull();
    }

    [Fact]
    public void Push_Preference_Defaults_Off_And_Is_Cleared_On_Account_Deletion()
    {
        var account = OwnerAccount.Create("a@b.com", "Ayşe", Now);
        account.PushEnabled.Should().BeFalse();

        account.SetPushEnabled(true).Should().BeTrue();
        account.SetPushEnabled(true).Should().BeFalse();
        account.Delete(Now).IsSuccess.Should().BeTrue();

        account.PushEnabled.Should().BeFalse();
    }

    [Fact]
    public void DataRequest_Is_Due_In_30_Days_And_Follows_Lifecycle()
    {
        var link = NewLink();
        var request = OwnerDataRequest.Create(link, OwnerDataRequestType.Erasure, "  Kayıtlarımın silinmesini istiyorum  ", Now).Value!;

        request.Status.Should().Be(OwnerDataRequestStatus.Open);
        request.Message.Should().Be("Kayıtlarımın silinmesini istiyorum");
        request.DueAtUtc.Should().Be(Now.AddDays(OwnerDataRequest.ResponseDueDays));
        request.ClinicId.Should().Be(link.ClinicId);
        request.LinkId.Should().Be(link.Id);
        request.IsOverdue(Now.AddDays(31)).Should().BeTrue();

        var userId = Guid.NewGuid();
        request.Handle(OwnerDataRequestStatus.InProgress, null, userId, Now).IsSuccess.Should().BeTrue();
        request.Cancel(Now).Error.Code.Should().Be("Portal.DataRequests.NotOpen");
        request.Handle(OwnerDataRequestStatus.Completed, null, userId, Now).Error.Code.Should().Be("PortalInbox.ResponseNoteRequired");
        request.Handle(OwnerDataRequestStatus.Completed, "Yasal saklama süresi dolmayan kayıtlar dışında silindi.", userId, Now)
            .IsSuccess.Should().BeTrue();
        request.IsOpen.Should().BeFalse();
        request.IsOverdue(Now.AddDays(31)).Should().BeFalse();
        request.Handle(OwnerDataRequestStatus.Rejected, "x", userId, Now).Error.Code.Should().Be("PortalInbox.InvalidStatusTransition");
    }

    [Fact]
    public void DataRequest_Requires_Message_And_Open_Can_Be_Cancelled()
    {
        OwnerDataRequest.Create(NewLink(), OwnerDataRequestType.Access, "  ", Now).Error.Code.Should().Be("Portal.DataRequests.Validation");
        OwnerDataRequest.Create(NewLink(), (OwnerDataRequestType)99, "x", Now).Error.Code.Should().Be("Portal.DataRequests.Validation");

        var request = OwnerDataRequest.Create(NewLink(), OwnerDataRequestType.Access, "Kopya istiyorum", Now).Value!;
        request.Cancel(Now).IsSuccess.Should().BeTrue();
        request.Status.Should().Be(OwnerDataRequestStatus.Cancelled);
        request.Handle(OwnerDataRequestStatus.InProgress, null, Guid.NewGuid(), Now).Error.Code.Should().Be("PortalInbox.InvalidStatusTransition");
    }

    [Fact]
    public void Clinic_Portal_Is_Enabled_By_Default()
    {
        ClinicPortalSettings.IsEnabledOrDefault(null).Should().BeTrue();
        var settings = ClinicPortalSettings.Create(Guid.NewGuid(), Guid.NewGuid(), Now);
        settings.SetEnabled(false, Guid.NewGuid(), Now);
        ClinicPortalSettings.IsEnabledOrDefault(settings).Should().BeFalse();
    }

    [Theory]
    [InlineData(OwnerNotificationType.SignInCode, OwnerNotificationChannel.Email, OwnerNotificationChannelMode.Always)]
    [InlineData(OwnerNotificationType.SignInCode, OwnerNotificationChannel.Push, OwnerNotificationChannelMode.Disabled)]
    [InlineData(OwnerNotificationType.PortalInvite, OwnerNotificationChannel.WhatsAppLink, OwnerNotificationChannelMode.StaffManual)]
    [InlineData(OwnerNotificationType.DataRequestAnswered, OwnerNotificationChannel.Push, OwnerNotificationChannelMode.NotConfigured)]
    [InlineData(OwnerNotificationType.DataRequestAnswered, OwnerNotificationChannel.WhatsAppLink, OwnerNotificationChannelMode.Disabled)]
    [InlineData(OwnerNotificationType.VaccinationReminder, OwnerNotificationChannel.Email, OwnerNotificationChannelMode.Preference)]
    [InlineData(OwnerNotificationType.VaccinationReminder, OwnerNotificationChannel.WhatsAppLink, OwnerNotificationChannelMode.StaffManual)]
    [InlineData(OwnerNotificationType.Marketing, OwnerNotificationChannel.Email, OwnerNotificationChannelMode.Disabled)]
    [InlineData(OwnerNotificationType.Marketing, OwnerNotificationChannel.Push, OwnerNotificationChannelMode.Disabled)]
    [InlineData(OwnerNotificationType.FollowUpReminder, OwnerNotificationChannel.Email, OwnerNotificationChannelMode.Preference)]
    [InlineData(OwnerNotificationType.DebtReminder, OwnerNotificationChannel.Email, OwnerNotificationChannelMode.Preference)]
    [InlineData(OwnerNotificationType.DebtReminder, OwnerNotificationChannel.Push, OwnerNotificationChannelMode.Disabled)]
    [InlineData(OwnerNotificationType.DebtReminder, OwnerNotificationChannel.WhatsAppLink, OwnerNotificationChannelMode.Disabled)]
    [InlineData(OwnerNotificationType.NoReturnReminder, OwnerNotificationChannel.Email, OwnerNotificationChannelMode.Disabled)]
    public void Channel_Policy_Matrix(OwnerNotificationType type, OwnerNotificationChannel channel, OwnerNotificationChannelMode expected)
        => OwnerNotificationChannelPolicy.ModeOf(type, channel, pushConfigured: false).Should().Be(expected);

    [Fact]
    public void Transactional_Email_Ignores_Preference_And_Push_Needs_Provider_And_OptIn()
    {
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.DataRequestAnswered, OwnerNotificationChannel.Email, false, false)
            .Should().BeTrue();
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.DataRequestAnswered, OwnerNotificationChannel.Push, false, true)
            .Should().BeFalse();
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.DataRequestAnswered, OwnerNotificationChannel.Push, true, true)
            .Should().BeTrue();
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.AppointmentReminder, OwnerNotificationChannel.Email, false, false)
            .Should().BeFalse();
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.Marketing, OwnerNotificationChannel.Email, true, true)
            .Should().BeFalse();
        OwnerNotificationChannelPolicy.ShouldSend(OwnerNotificationType.AppointmentReminder, OwnerNotificationChannel.WhatsAppLink, true, true)
            .Should().BeFalse();
    }
}
