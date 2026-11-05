using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.OwnerPortal;

/// <summary>MOBILE-003: cihaz jetonu normalleştirme, sınır ve yeniden kayıtta hesap/platform yenileme.</summary>
public sealed class OwnerPushDeviceTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Token_Is_Trimmed_And_Bounded()
    {
        OwnerPushDevice.NormalizeToken("  abc  ").Should().Be("abc");
        OwnerPushDevice.NormalizeToken("   ").Should().BeNull();
        OwnerPushDevice.NormalizeToken(null).Should().BeNull();
        OwnerPushDevice.NormalizeToken(new string('x', OwnerPushDevice.MaxTokenLength)).Should().HaveLength(OwnerPushDevice.MaxTokenLength);
        OwnerPushDevice.NormalizeToken(new string('x', OwnerPushDevice.MaxTokenLength + 1)).Should().BeNull();

        var act = () => OwnerPushDevice.Create(Guid.NewGuid(), OwnerPushPlatform.Android, " ", Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Refresh_Moves_Device_To_Account_And_Updates_Platform_And_Last_Seen()
    {
        var device = OwnerPushDevice.Create(Guid.NewGuid(), OwnerPushPlatform.Android, " tok ", Now);
        device.Token.Should().Be("tok");
        device.CreatedAtUtc.Should().Be(Now);
        device.LastSeenAtUtc.Should().Be(Now);

        var other = Guid.NewGuid();
        device.Refresh(other, OwnerPushPlatform.Web, Now.AddDays(1));

        device.OwnerAccountId.Should().Be(other);
        device.Platform.Should().Be(OwnerPushPlatform.Web);
        device.LastSeenAtUtc.Should().Be(Now.AddDays(1));
        device.CreatedAtUtc.Should().Be(Now);
    }
}
