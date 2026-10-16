using Backend.Veteriner.Domain.OwnerPortal;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.OwnerPortal;

/// <summary>ADR-031: tek kullanımlık kod süresi, deneme hakkı, tek kullanım ve bekleme.</summary>
public sealed class OwnerPortalCodeTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static OwnerPortalCode NewCode() =>
        OwnerPortalCode.Issue(Guid.NewGuid(), Guid.NewGuid(), OwnerPortalCodePurpose.SignIn, new string('a', 64), Now);

    [Fact]
    public void Issue_Should_Expire_After_Lifetime()
    {
        var code = NewCode();

        code.ExpiresAtUtc.Should().Be(Now.Add(OwnerPortalCode.Lifetime));
        code.IsActive(Now.Add(OwnerPortalCode.Lifetime).AddSeconds(-1)).Should().BeTrue();
        code.IsActive(Now.Add(OwnerPortalCode.Lifetime)).Should().BeFalse();
    }

    [Fact]
    public void Matching_Code_Should_Be_Consumed_Once()
    {
        var code = NewCode();

        code.TryConsume(codeMatches: true, Now).Should().BeTrue();
        code.ConsumedAtUtc.Should().Be(Now);
        code.TryConsume(codeMatches: true, Now).Should().BeFalse("kod tek kullanımlıktır");
    }

    [Fact]
    public void Wrong_Attempts_Should_Invalidate_Code_After_Limit()
    {
        var code = NewCode();

        for (var i = 0; i < OwnerPortalCode.MaxFailedAttempts; i++)
            code.TryConsume(codeMatches: false, Now).Should().BeFalse();

        code.FailedAttempts.Should().Be(OwnerPortalCode.MaxFailedAttempts);
        code.InvalidatedAtUtc.Should().Be(Now);
        code.TryConsume(codeMatches: true, Now).Should().BeFalse("deneme hakkı bitince doğru kod da geçmez");
    }

    [Fact]
    public void Expired_Code_Should_Not_Count_Attempts()
    {
        var code = NewCode();
        var later = Now.Add(OwnerPortalCode.Lifetime).AddMinutes(1);

        code.TryConsume(codeMatches: true, later).Should().BeFalse();
        code.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public void Invalidated_Code_Should_Not_Be_Consumed()
    {
        var code = NewCode();

        code.Invalidate(Now);

        code.TryConsume(codeMatches: true, Now).Should().BeFalse();
    }

    [Fact]
    public void Resend_Cooldown_Should_Hold_For_Sixty_Seconds()
    {
        var code = NewCode();

        code.IsInResendCooldown(Now.AddSeconds(59)).Should().BeTrue();
        code.IsInResendCooldown(Now.Add(OwnerPortalCode.ResendCooldown)).Should().BeFalse();
    }
}
