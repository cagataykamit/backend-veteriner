using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Tenants;

public sealed class SignupPhoneVerificationTests
{
    private const string Phone = "905321234567";
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private static SignupPhoneVerification Issue(DateTime? at = null)
        => SignupPhoneVerification.Issue(Guid.NewGuid(), Phone, "kod-ozeti", "ip-ozeti", at ?? Now);

    [Fact]
    public void Issue_Should_Set_Five_Minute_Lifetime_And_Reject_Non_Normalized_Phone()
    {
        var v = Issue();

        v.ExpiresAtUtc.Should().Be(Now.AddMinutes(5));
        v.IsCodeActive(Now.AddMinutes(4).AddSeconds(59)).Should().BeTrue();
        v.IsCodeActive(Now.AddMinutes(5)).Should().BeFalse();
        FluentActions.Invoking(() => SignupPhoneVerification.Issue(Guid.NewGuid(), "05321234567", "x", null, Now))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resend_Cooldown_Should_Be_Sixty_Seconds()
    {
        var v = Issue();

        v.IsInResendCooldown(Now.AddSeconds(59)).Should().BeTrue();
        v.ResendAvailableIn(Now.AddSeconds(20)).Should().Be(TimeSpan.FromSeconds(40));
        v.IsInResendCooldown(Now.AddSeconds(60)).Should().BeFalse();
        v.ResendAvailableIn(Now.AddSeconds(90)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TryVerify_Should_Invalidate_After_Five_Wrong_Attempts()
    {
        var v = Issue();

        for (var i = 0; i < 4; i++)
            v.TryVerify(false, "jeton", Now).Should().BeFalse();
        v.IsCodeActive(Now).Should().BeTrue();

        v.TryVerify(false, "jeton", Now).Should().BeFalse();
        v.IsCodeActive(Now).Should().BeFalse();
        v.TryVerify(true, "jeton", Now).Should().BeFalse("hak bitince doğru kod da kabul edilmez");
        v.InvalidatedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void TryVerify_Should_Issue_Signup_Token_Once()
    {
        var v = Issue();

        v.TryVerify(true, "jeton-ozeti", Now.AddMinutes(1)).Should().BeTrue();

        v.VerifiedAtUtc.Should().Be(Now.AddMinutes(1));
        v.SignupTokenHash.Should().Be("jeton-ozeti");
        v.SignupTokenExpiresAtUtc.Should().Be(Now.AddMinutes(31));
        v.TryVerify(true, "baska", Now.AddMinutes(2)).Should().BeFalse("kod tek kullanımlık");
    }

    [Fact]
    public void Expired_Code_Should_Not_Verify()
    {
        var v = Issue();

        v.TryVerify(true, "jeton", Now.AddMinutes(6)).Should().BeFalse();
        v.FailedAttempts.Should().Be(0);
    }

    [Fact]
    public void Signup_Use_Should_Require_Verified_Unexpired_Unused_Row()
    {
        var v = Issue();
        v.CanBeUsedForSignup(Now).Should().BeFalse();
        FluentActions.Invoking(() => v.MarkUsed(Guid.NewGuid(), Now)).Should().Throw<InvalidOperationException>();

        v.TryVerify(true, "jeton", Now);
        v.CanBeUsedForSignup(Now.AddMinutes(29)).Should().BeTrue();
        v.CanBeUsedForSignup(Now.AddMinutes(30)).Should().BeFalse("jeton 30 dakika geçerli");

        var tenantId = Guid.NewGuid();
        v.MarkUsed(tenantId, Now.AddMinutes(10));
        v.UsedByTenantId.Should().Be(tenantId);
        v.CanBeUsedForSignup(Now.AddMinutes(11)).Should().BeFalse();
    }

    [Fact]
    public void Invalidate_Should_Close_Verified_Token_But_Not_Used_Row()
    {
        var open = Issue();
        open.TryVerify(true, "jeton", Now);
        open.Invalidate(Now.AddMinutes(1));
        open.CanBeUsedForSignup(Now.AddMinutes(2)).Should().BeFalse();

        var used = Issue();
        used.TryVerify(true, "jeton", Now);
        used.MarkUsed(Guid.NewGuid(), Now);
        used.Invalidate(Now.AddMinutes(1));
        used.InvalidatedAtUtc.Should().BeNull("kullanılmış satır numaranın deneme hakkını kapatmaya devam eder");
    }

    [Theory]
    [InlineData("0532 123 45 67", "90532*****67")]
    [InlineData("+90 (532) 123-45-67", "90532*****67")]
    [InlineData("0212 123 45 67", "***")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void TurkishMobilePhone_Mask_Should_Keep_Operator_And_Last_Two_Digits(string? input, string? expected)
        => TurkishMobilePhone.Mask(input).Should().Be(expected);
}
