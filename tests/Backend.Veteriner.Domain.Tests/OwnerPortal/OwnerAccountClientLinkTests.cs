using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.OwnerPortal;

/// <summary>ADR-031 K3 ve ADR-041 K4: bağ kapatma ve kapanma gerekçesi.</summary>
public sealed class OwnerAccountClientLinkTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private static OwnerAccountClientLink NewLink()
        => OwnerAccountClientLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1));

    [Fact]
    public void Close_By_Staff_Should_Record_User_And_ClinicClosed_Reason()
    {
        var link = NewLink();
        var userId = Guid.NewGuid();

        link.Close(userId, Now).IsSuccess.Should().BeTrue();

        link.Status.Should().Be(OwnerAccountClientLinkStatus.Closed);
        link.ClosedByUserId.Should().Be(userId);
        link.CloseReason.Should().Be(OwnerAccountClientLinkCloseReason.ClinicClosed);
        link.ClosedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void CloseByOwnerDeletion_Should_Keep_Disclosure_And_Leave_User_Empty()
    {
        var link = NewLink();
        link.AcknowledgeDisclosure(Now.AddHours(-2));

        link.CloseByOwnerDeletion(Now).IsSuccess.Should().BeTrue();

        link.IsActive.Should().BeFalse();
        link.ClosedByUserId.Should().BeNull();
        link.CloseReason.Should().Be(OwnerAccountClientLinkCloseReason.OwnerAccountDeleted);
        link.ClosedAtUtc.Should().Be(Now);
        link.DisclosureAcknowledgedAtUtc.Should().Be(Now.AddHours(-2));
    }

    [Fact]
    public void CloseByOwnerDeletion_On_Closed_Link_Should_Fail_And_Keep_Reason()
    {
        var link = NewLink();
        link.Close(Guid.NewGuid(), Now.AddHours(-1));

        var result = link.CloseByOwnerDeletion(Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("OwnerPortal.LinkAlreadyClosed");
        link.CloseReason.Should().Be(OwnerAccountClientLinkCloseReason.ClinicClosed);
    }
}
