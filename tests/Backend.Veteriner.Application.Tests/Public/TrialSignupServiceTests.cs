using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Security;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Public;

public sealed class TrialSignupServiceTests
{
    private const string Email = TrialSignupTestSupport.Email;
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly TrialSignupTestSupport _s = new();

    [Fact]
    public async Task RequestCode_Should_Email_Code_And_Store_Only_Hash()
    {
        var result = await _s.CreateEmailService().RequestCodeAsync(Email, "203.0.113.5", Now, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpiresInSeconds.Should().Be(600);
        result.Value.ResendAfterSeconds.Should().Be(60);
        _s.EmailSender.Verify(e => e.SendAsync(Email, It.IsAny<string>(), It.Is<string>(b => b.Contains("123456")),
            It.IsAny<CancellationToken>(), false), Times.Once);
        var row = _s.Verifications.Single();
        row.CodeHash.Should().StartWith(row.Id.ToString("N")).And.NotBe("123456");
        _s.Hash.Verify(h => h.ComputeSha256("signup-email-ip:203.0.113.5"), Times.Once, "IP düz saklanmaz, özetlenir");
    }

    [Fact]
    public async Task RequestCode_Should_Return_ResendTooSoon_And_Invalidate_On_Later_Resend()
    {
        var service = _s.CreateEmailService();
        await service.RequestCodeAsync(Email, null, Now, CancellationToken.None);

        var again = await service.RequestCodeAsync(Email, null, Now.AddSeconds(45), CancellationToken.None);
        again.Error.Code.Should().Be("SignupEmail.ResendTooSoon");
        again.Error.Extensions!["retryAfterSeconds"].Should().Be(15);

        (await service.RequestCodeAsync(Email, null, Now.AddSeconds(61), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _s.Verifications.OrderBy(v => v.CreatedAtUtc).First().InvalidatedAtUtc.Should().Be(Now.AddSeconds(61));
    }

    [Fact]
    public async Task RequestCode_Should_Enforce_Per_Email_Per_Ip_And_Global_Daily_Limits()
    {
        var service = _s.CreateEmailService();
        for (var i = 0; i < SignupEmailVerification.MaxCodesPerEmailPerDay; i++)
            (await service.RequestCodeAsync(Email, null, Now.AddMinutes(i * 2), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await service.RequestCodeAsync(Email, null, Now.AddMinutes(20), CancellationToken.None))
            .Error.Code.Should().Be("SignupEmail.DailyLimitReached");

        for (var i = 0; i < SignupEmailVerification.MaxCodesPerIpPerDay; i++)
            (await service.RequestCodeAsync($"ip{i}@a.com", "198.51.100.7", Now, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await service.RequestCodeAsync("ip-son@a.com", "198.51.100.7", Now, CancellationToken.None))
            .Error.Code.Should().Be("SignupEmail.DailyLimitReached");

        for (var i = _s.Verifications.Count; i < SignupEmailVerification.MaxCodesPerDay; i++)
            _s.Verifications.Add(SignupEmailVerification.Issue(Guid.NewGuid(), $"dolgu{i}@a.com", "x", null, Now.AddHours(-1)));
        (await service.RequestCodeAsync("yeni@a.com", null, Now, CancellationToken.None))
            .Error.Code.Should().Be("SignupEmail.DailyLimitReached");
    }

    [Fact]
    public async Task VerifyCode_Should_Count_Wrong_Attempt_And_Return_Token_On_Match()
    {
        var service = _s.CreateEmailService();
        await service.RequestCodeAsync(Email, null, Now, CancellationToken.None);

        (await service.VerifyCodeAsync(Email, "654321", Now.AddMinutes(1), CancellationToken.None))
            .Error.Code.Should().Be("SignupEmail.CodeInvalid");
        _s.Verifications.Single().FailedAttempts.Should().Be(1);

        var ok = await service.VerifyCodeAsync(Email, " 123456 ", Now.AddMinutes(2), CancellationToken.None);
        ok.Value!.EmailVerificationToken.Should().Be(TrialSignupTestSupport.Token);
        ok.Value.ExpiresAtUtc.Should().Be(Now.AddMinutes(32));
        _s.VerificationRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));

        (await service.FindForSignupAsync(Email, TrialSignupTestSupport.Token, Now.AddMinutes(10), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await service.FindForSignupAsync(Email, "baska", Now.AddMinutes(10), CancellationToken.None))
            .Error.Code.Should().Be("SignupEmail.VerificationRequired");
        (await service.FindForSignupAsync("baska@a.com", TrialSignupTestSupport.Token, Now.AddMinutes(10), CancellationToken.None))
            .Error.Code.Should().Be("SignupEmail.VerificationRequired");
    }

    [Fact]
    public async Task EnsureEmailAvailable_Should_Reject_Used_Email_Plus_Alias_And_Existing_User()
    {
        _s.Identities.Add(TrialSignupIdentity.Create(Guid.NewGuid(), "ad@ornek.com", null, Now));
        var service = _s.CreateIdentityService();

        (await service.EnsureEmailAvailableAsync("Ad+ikinci@Ornek.com", CancellationToken.None))
            .Error.Code.Should().Be("SignupIdentity.DuplicateEmail");

        _s.Users.Setup(u => u.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        (await service.EnsureEmailAvailableAsync("kayitli@ornek.com", CancellationToken.None))
            .Error.Code.Should().Be("SignupIdentity.DuplicateEmail");
    }

    [Theory]
    [InlineData(TrialSignupTestSupport.ValidVkn)]
    [InlineData(TrialSignupTestSupport.ValidTckn)]
    public async Task NormalizeAvailableTaxIdentity_Should_Accept_Valid_Vkn_And_Tckn(string value)
        => (await _s.CreateIdentityService().NormalizeAvailableTaxIdentityAsync($" {value} ", CancellationToken.None))
            .Value.Should().Be(value);

    [Theory]
    [InlineData(null)]
    [InlineData("1234567891")]
    [InlineData("10000000147")]
    [InlineData("12345")]
    public async Task NormalizeAvailableTaxIdentity_Should_Reject_Missing_Or_Invalid_When_Required(string? value)
        => (await _s.CreateIdentityService().NormalizeAvailableTaxIdentityAsync(value, CancellationToken.None))
            .Error.Code.Should().Be("SignupIdentity.Validation.TaxIdentityRequired");

    [Fact]
    public async Task NormalizeAvailableTaxIdentity_Should_Allow_Empty_When_Not_Required_And_Reject_Used_Number()
    {
        _s.Options = new() { RequireTaxIdentityNumber = false };
        (await _s.CreateIdentityService().NormalizeAvailableTaxIdentityAsync(" ", CancellationToken.None)).Value.Should().BeNull();

        _s.Identities.Add(TrialSignupIdentity.Create(Guid.NewGuid(), "x@a.com", TrialSignupTestSupport.ValidVkn, Now));
        (await _s.CreateIdentityService().NormalizeAvailableTaxIdentityAsync(TrialSignupTestSupport.ValidVkn, CancellationToken.None))
            .Error.Code.Should().Be("SignupIdentity.DuplicateTaxIdentity");
    }

    [Theory]
    [InlineData(true, HumanVerificationResult.Passed, null)]
    [InlineData(true, HumanVerificationResult.Failed, "HumanVerification.Failed")]
    [InlineData(true, HumanVerificationResult.Unavailable, "HumanVerification.Unavailable")]
    [InlineData(false, HumanVerificationResult.Passed, "HumanVerification.Unavailable")]
    public async Task HumanVerificationGuard_Should_Map_Result(bool configured, HumanVerificationResult outcome, string? expectedCode)
    {
        var verifier = new Mock<IHumanVerifier>();
        verifier.SetupGet(v => v.IsConfigured).Returns(configured);
        verifier.Setup(v => v.VerifyAsync("jeton", "203.0.113.5", "eylem", It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        var client = new Mock<IClientContext>();
        client.SetupGet(c => c.IpAddress).Returns("203.0.113.5");

        var result = await new HumanVerificationGuard(verifier.Object, client.Object).EnsureHumanAsync("jeton", "eylem", CancellationToken.None);

        if (expectedCode is null)
            result.IsSuccess.Should().BeTrue();
        else
            result.Error.Code.Should().Be(expectedCode);
    }
}
