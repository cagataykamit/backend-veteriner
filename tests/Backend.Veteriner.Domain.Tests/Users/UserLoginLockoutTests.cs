using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Users;

public sealed class UserLoginLockoutTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private static User NewUser() => new("ali.veli@example.com", "hash", Now);

    [Fact]
    public void RecordFailedLogin_Should_Lock_On_Max_Attempts_And_Report_It_Once()
    {
        var user = NewUser();

        for (var i = 0; i < SignInLockout.MaxFailedAttempts - 1; i++)
            user.RecordFailedLogin(Now).Should().BeFalse();
        user.IsLockedOut(Now).Should().BeFalse();

        user.RecordFailedLogin(Now).Should().BeTrue();
        user.IsLockedOut(Now).Should().BeTrue();
        user.FailedLoginCount.Should().Be(0);

        // Kilitliyken deneme kilidi uzatmaz ve yeniden kilit olayı üretmez.
        user.RecordFailedLogin(Now.AddMinutes(5)).Should().BeFalse();
        user.LockedUntilUtc.Should().Be(Now.Add(SignInLockout.Duration));
    }

    [Fact]
    public void Lock_Should_Expire_After_Duration()
    {
        var user = NewUser();
        for (var i = 0; i < SignInLockout.MaxFailedAttempts; i++)
            user.RecordFailedLogin(Now);

        user.IsLockedOut(Now.Add(SignInLockout.Duration)).Should().BeFalse();
    }

    [Fact]
    public void ClearLockout_Should_Reset_Counter_And_Lock()
    {
        var user = NewUser();
        for (var i = 0; i < SignInLockout.MaxFailedAttempts; i++)
            user.RecordFailedLogin(Now);
        user.RecordFailedLogin(Now);

        user.ClearLockout();

        user.FailedLoginCount.Should().Be(0);
        user.IsLockedOut(Now).Should().BeFalse();
    }

    [Fact]
    public void UpdatePasswordHash_Should_Clear_Lockout()
    {
        var user = NewUser();
        for (var i = 0; i < SignInLockout.MaxFailedAttempts; i++)
            user.RecordFailedLogin(Now);

        user.UpdatePasswordHash("new-hash", Now);

        user.IsLockedOut(Now).Should().BeFalse();
    }
}
