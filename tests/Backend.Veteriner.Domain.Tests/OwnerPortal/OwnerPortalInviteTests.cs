using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.OwnerPortal;

/// <summary>ADR-031 K3/K4: davet tek kullanımlık/süreli, uygunluk kuralı (KVKK Ret, RemindersOptedOut), bağ kapatma.</summary>
public sealed class OwnerPortalInviteTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static OwnerPortalInvite NewInvite(DateTime? expires = null) => OwnerPortalInvite.CreatePending(
        TenantId, Guid.NewGuid(), Guid.NewGuid(), " Ayse@Example.com ", new string('h', 64), Guid.NewGuid(),
        expires ?? Now.AddDays(7), Now);

    [Fact]
    public void CreatePending_Should_Normalize_Email_And_Reject_Past_Expiry()
    {
        NewInvite().Email.Should().Be("ayse@example.com");

        var act = () => NewInvite(Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Accept_Should_Be_Single_Use()
    {
        var invite = NewInvite();
        var accountId = Guid.NewGuid();

        invite.Accept(accountId, Now.AddHours(1)).IsSuccess.Should().BeTrue();
        invite.Status.Should().Be(OwnerPortalInviteStatus.Accepted);
        invite.AcceptedOwnerAccountId.Should().Be(accountId);

        var second = invite.Accept(Guid.NewGuid(), Now.AddHours(2));
        second.IsSuccess.Should().BeFalse();
        second.Error.Code.Should().Be("Portal.Invites.NotFound");
    }

    [Fact]
    public void Expired_Invite_Should_Not_Be_Accepted()
    {
        var invite = NewInvite();

        invite.IsUsable(Now.AddDays(7)).Should().BeFalse();
        invite.Accept(Guid.NewGuid(), Now.AddDays(7)).Error.Code.Should().Be("Portal.Invites.NotFound");
    }

    [Fact]
    public void Revoke_Should_Only_Apply_To_Pending()
    {
        var invite = NewInvite();
        var userId = Guid.NewGuid();

        invite.Revoke(userId, Now).IsSuccess.Should().BeTrue();
        invite.RevokedByUserId.Should().Be(userId);
        invite.Revoke(userId, Now).Error.Code.Should().Be("OwnerPortal.InviteNotPending");
        invite.Accept(Guid.NewGuid(), Now).IsSuccess.Should().BeFalse("iptal edilmiş davet kullanılamaz");
    }

    [Fact]
    public void Policy_Should_Allow_Client_With_Email_And_No_OptOut()
        => OwnerPortalInvitePolicy.CanInvite(new Client(TenantId, "Ayşe Yılmaz", email: "a@b.com")).IsSuccess.Should().BeTrue();

    [Fact]
    public void Policy_Should_Reject_Client_Without_Email()
        => OwnerPortalInvitePolicy.CanInvite(new Client(TenantId, "Ayşe Yılmaz")).Error.Code
            .Should().Be("OwnerPortal.ClientEmailMissing");

    [Fact]
    public void Policy_Should_Reject_Kvkk_Declined_Client()
    {
        var client = new Client(TenantId, "Ayşe Yılmaz", email: "a@b.com");
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, DateOnly.FromDateTime(Now), Guid.NewGuid(), null, null);

        OwnerPortalInvitePolicy.CanInvite(client).Error.Code.Should().Be("OwnerPortal.ClientOptedOut");
    }

    [Fact]
    public void Policy_Should_Reject_Client_With_Reminders_Opted_Out()
    {
        var client = new Client(TenantId, "Ayşe Yılmaz", email: "a@b.com");
        client.ApplyContactPreferences(null, remindersOptedOut: true);

        OwnerPortalInvitePolicy.CanInvite(client).Error.Code.Should().Be("OwnerPortal.ClientOptedOut");
    }

    [Fact]
    public void Policy_Should_Reject_WalkIn_Client()
        => OwnerPortalInvitePolicy.CanInvite(Client.CreateWalkIn(TenantId)).Error.Code
            .Should().Be("OwnerPortal.ClientNotInvitable");

    [Fact]
    public void Link_Close_Should_Be_Once_And_Acknowledge_Should_Keep_First_Time()
    {
        var link = OwnerAccountClientLink.Create(Guid.NewGuid(), TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        link.AcknowledgeDisclosure(Now.AddMinutes(1));
        link.AcknowledgeDisclosure(Now.AddMinutes(5));
        link.DisclosureAcknowledgedAtUtc.Should().Be(Now.AddMinutes(1));

        link.Close(Guid.NewGuid(), Now.AddHours(1)).IsSuccess.Should().BeTrue();
        link.IsActive.Should().BeFalse();
        link.Close(Guid.NewGuid(), Now.AddHours(2)).Error.Code.Should().Be("OwnerPortal.LinkAlreadyClosed");
    }
}
