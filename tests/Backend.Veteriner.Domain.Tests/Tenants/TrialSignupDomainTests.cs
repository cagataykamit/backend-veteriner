using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Tenants;

public sealed class TrialSignupDomainTests
{
    private const string Email = "sahip@ornek.com";
    private const string ValidVkn = "1234567890";
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private static SignupEmailVerification Issue() => SignupEmailVerification.Issue(Guid.NewGuid(), Email, "kod-ozeti", "ip-ozeti", Now);

    [Fact]
    public void Issue_Should_Set_Ten_Minute_Lifetime_And_Require_Normalized_Email()
    {
        var v = Issue();

        v.IsCodeActive(Now.AddMinutes(9).AddSeconds(59)).Should().BeTrue();
        v.IsCodeActive(Now.AddMinutes(10)).Should().BeFalse();
        FluentActions.Invoking(() => SignupEmailVerification.Issue(Guid.NewGuid(), " Sahip@Ornek.com", "x", null, Now))
            .Should().Throw<ArgumentException>();
        SignupEmailVerification.NormalizeEmail(" Sahip@Ornek.com ").Should().Be(Email);
    }

    [Fact]
    public void Resend_Cooldown_Should_Be_Sixty_Seconds()
    {
        var v = Issue();

        v.IsInResendCooldown(Now.AddSeconds(59)).Should().BeTrue();
        v.ResendAvailableIn(Now.AddSeconds(20)).Should().Be(TimeSpan.FromSeconds(40));
        v.IsInResendCooldown(Now.AddSeconds(60)).Should().BeFalse();
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
    }

    [Fact]
    public void Verified_Token_Should_Be_Usable_Once_Within_Thirty_Minutes()
    {
        var v = Issue();
        v.CanBeUsedForSignup(Now).Should().BeFalse();

        v.TryVerify(true, "jeton-ozeti", Now.AddMinutes(1)).Should().BeTrue();
        v.TryVerify(true, "baska", Now.AddMinutes(2)).Should().BeFalse("kod tek kullanımlık");
        v.CanBeUsedForSignup(Now.AddMinutes(30)).Should().BeTrue();
        v.CanBeUsedForSignup(Now.AddMinutes(31)).Should().BeFalse();

        v.MarkUsed(Now.AddMinutes(5));
        v.CanBeUsedForSignup(Now.AddMinutes(6)).Should().BeFalse();
        FluentActions.Invoking(() => v.MarkUsed(Now.AddMinutes(6))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Invalidate_Should_Close_Verified_Token()
    {
        var v = Issue();
        v.TryVerify(true, "jeton", Now);

        v.Invalidate(Now.AddMinutes(1));

        v.CanBeUsedForSignup(Now.AddMinutes(2)).Should().BeFalse();
    }

    [Theory]
    [InlineData(" Ad.Soyad+deneme2@Ornek.COM ", "ad.soyad@ornek.com")]
    [InlineData("ad.soyad@ornek.com", "ad.soyad@ornek.com")]
    [InlineData("+etiket@ornek.com", "+etiket@ornek.com")]
    [InlineData("", null)]
    public void ToEmailKey_Should_Drop_Plus_Tag_And_Case(string input, string? expected)
        => TrialSignupIdentity.ToEmailKey(input).Should().Be(expected);

    [Fact]
    public void Create_Should_Store_Normalized_Tax_Number_And_Reject_Invalid()
    {
        var identity = TrialSignupIdentity.Create(Guid.NewGuid(), "Sahip+x@Ornek.com", $" {ValidVkn} ", Now);

        identity.EmailKey.Should().Be("sahip@ornek.com");
        identity.TaxIdentityNumber.Should().Be(ValidVkn);
        TrialSignupIdentity.Create(Guid.NewGuid(), Email, null, Now).TaxIdentityNumber.Should().BeNull();
        FluentActions.Invoking(() => TrialSignupIdentity.Create(Guid.NewGuid(), Email, "1234567891", Now))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BillingProfile_CreateWithTaxNumber_Should_Set_Only_Tax_Number()
    {
        var tenantId = Guid.NewGuid();

        var profile = TenantBillingProfile.CreateWithTaxNumber(tenantId, $" {ValidVkn} ", Now);

        profile.TenantId.Should().Be(tenantId);
        profile.TaxNumber.Should().Be(ValidVkn);
        profile.CompanyName.Should().BeNull();
    }
}
