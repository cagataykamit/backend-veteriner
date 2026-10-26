using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.OwnerPortal;

/// <summary>ADR-031: portal hesabı e-posta normalizasyonu ve kilit kuralı.</summary>
public sealed class OwnerAccountTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Should_Normalize_Email_And_Be_Active_Without_Password()
    {
        var account = OwnerAccount.Create("  Ayse@Example.COM ", " Ayşe ", Now);

        account.Email.Should().Be("ayse@example.com");
        account.DisplayName.Should().Be("Ayşe");
        account.IsActive.Should().BeTrue();
        account.HasPassword.Should().BeFalse();
        account.CanAttemptSignIn(Now).Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Truncate_Long_DisplayName()
    {
        var account = OwnerAccount.Create("a@b.com", new string('x', 300), Now);

        account.DisplayName.Should().HaveLength(OwnerAccount.MaxDisplayNameLength);
    }

    [Fact]
    public void Create_Should_Reject_Empty_Email()
    {
        var act = () => OwnerAccount.Create(" ", "Ad", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Five_Failed_SignIns_Should_Lock_Account_For_Fifteen_Minutes()
    {
        var account = OwnerAccount.Create("a@b.com", "Ad", Now);

        for (var i = 0; i < OwnerAccount.MaxFailedSignInAttempts - 1; i++)
            account.RecordFailedSignIn(Now);
        account.IsLockedOut(Now).Should().BeFalse();

        account.RecordFailedSignIn(Now);

        account.IsLockedOut(Now).Should().BeTrue();
        account.CanAttemptSignIn(Now.AddMinutes(14)).Should().BeFalse();
        account.CanAttemptSignIn(Now.Add(OwnerAccount.LockoutDuration)).Should().BeTrue();
        account.FailedSignInCount.Should().Be(0);
    }

    [Fact]
    public void Failed_SignIn_While_Locked_Should_Not_Extend_Lock()
    {
        var account = OwnerAccount.Create("a@b.com", "Ad", Now);
        for (var i = 0; i < OwnerAccount.MaxFailedSignInAttempts; i++)
            account.RecordFailedSignIn(Now);
        var lockedUntil = account.LockedUntilUtc;

        account.RecordFailedSignIn(Now.AddMinutes(10));

        account.LockedUntilUtc.Should().Be(lockedUntil);
        account.FailedSignInCount.Should().Be(0);
    }

    [Fact]
    public void Successful_SignIn_Should_Reset_Counter_And_Record_Time()
    {
        var account = OwnerAccount.Create("a@b.com", "Ad", Now);
        account.RecordFailedSignIn(Now);
        account.RecordFailedSignIn(Now);

        account.RecordSuccessfulSignIn(Now.AddMinutes(1));

        account.FailedSignInCount.Should().Be(0);
        account.LockedUntilUtc.Should().BeNull();
        account.LastLoginAtUtc.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void ClearLockout_Should_Unlock()
    {
        var account = OwnerAccount.Create("a@b.com", "Ad", Now);
        for (var i = 0; i < OwnerAccount.MaxFailedSignInAttempts; i++)
            account.RecordFailedSignIn(Now);

        account.ClearLockout();

        account.CanAttemptSignIn(Now).Should().BeTrue();
    }

    [Fact]
    public void Disabled_Account_Cannot_SignIn()
    {
        var account = OwnerAccount.Create("a@b.com", "Ad", Now);

        account.Disable();

        account.CanAttemptSignIn(Now).Should().BeFalse();
    }
}
