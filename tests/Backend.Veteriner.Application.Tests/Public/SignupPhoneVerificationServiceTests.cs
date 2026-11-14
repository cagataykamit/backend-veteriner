using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.PhoneVerification;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Public;

public sealed class SignupPhoneVerificationServiceTests
{
    private const string Phone = SignupPhoneVerificationTestSupport.Phone;
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly SignupPhoneVerificationTestSupport _support = new();

    [Fact]
    public async Task RequestCode_Should_Send_Sms_And_Store_Only_Hash()
    {
        var result = await _support.CreateService().RequestCodeAsync(Phone, "203.0.113.5", Now, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ExpiresInSeconds.Should().Be(300);
        result.Value.ResendAfterSeconds.Should().Be(60);
        _support.Sms.Verify(s => s.SendOneTimeCodeAsync(
            Phone, It.Is<string>(t => t.Contains("123456") && t.All(c => c < 128)), It.IsAny<CancellationToken>()), Times.Once);
        var row = _support.Rows.Single();
        row.CodeHash.Should().NotBe("123456").And.Contain("123456", "test özeti girdiyi aynen döndürür; tuz satır kimliğidir")
            .And.StartWith(row.Id.ToString("N"));
        _support.Hash.Verify(h => h.ComputeSha256("signup-phone-ip:203.0.113.5"), Times.Once, "IP düz saklanmaz, özetlenir");
        row.RequestIpHash.Should().NotBeNull();
        _support.Repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestCode_Should_Return_ResendTooSoon_Within_60_Seconds()
    {
        var service = _support.CreateService();
        await service.RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        var again = await service.RequestCodeAsync(Phone, null, Now.AddSeconds(45), CancellationToken.None);

        again.IsSuccess.Should().BeFalse();
        again.Error.Code.Should().Be("SignupPhone.ResendTooSoon");
        again.Error.Extensions!["retryAfterSeconds"].Should().Be(15);
        _support.Sms.Verify(s => s.SendOneTimeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RequestCode_Should_Invalidate_Previous_Code_On_Resend()
    {
        var service = _support.CreateService();
        await service.RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        (await service.RequestCodeAsync(Phone, null, Now.AddSeconds(61), CancellationToken.None)).IsSuccess.Should().BeTrue();

        _support.Rows.Should().HaveCount(2);
        _support.Rows.OrderBy(r => r.CreatedAtUtc).First().InvalidatedAtUtc.Should().Be(Now.AddSeconds(61));
    }

    [Fact]
    public async Task RequestCode_Should_Enforce_Per_Phone_Daily_Limit()
    {
        var service = _support.CreateService();
        for (var i = 0; i < SignupPhoneVerification.MaxCodesPerPhonePerDay; i++)
            (await service.RequestCodeAsync(Phone, null, Now.AddMinutes(i * 2), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var over = await service.RequestCodeAsync(Phone, null, Now.AddMinutes(20), CancellationToken.None);
        over.Error.Code.Should().Be("SignupPhone.DailyLimitReached");

        (await service.RequestCodeAsync(Phone, null, Now.AddHours(24).AddMinutes(1), CancellationToken.None))
            .IsSuccess.Should().BeTrue("pencere kayan 24 saattir");
    }

    [Fact]
    public async Task RequestCode_Should_Enforce_Per_Ip_Daily_Limit()
    {
        var service = _support.CreateService();
        for (var i = 0; i < SignupPhoneVerification.MaxCodesPerIpPerDay; i++)
            (await service.RequestCodeAsync($"90532{i:D7}", "198.51.100.7", Now, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var over = await service.RequestCodeAsync("905339999999", "198.51.100.7", Now, CancellationToken.None);
        over.Error.Code.Should().Be("SignupPhone.DailyLimitReached");
        (await service.RequestCodeAsync("905339999999", "198.51.100.8", Now, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequestCode_Should_Enforce_Global_Daily_Limit()
    {
        for (var i = 0; i < SignupPhoneVerification.MaxCodesPerDay; i++)
            _support.Rows.Add(SignupPhoneVerification.Issue(Guid.NewGuid(), $"90542{i:D7}", "x", null, Now.AddHours(-1)));

        var result = await _support.CreateService().RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        result.Error.Code.Should().Be("SignupPhone.DailyLimitReached");
        _support.Sms.Verify(s => s.SendOneTimeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestCode_Should_Return_DuplicateTrial_Without_Sms_When_Phone_Used()
    {
        _support.AddVerified(Now.AddDays(-3)).MarkUsed(Guid.NewGuid(), Now.AddDays(-3));

        var result = await _support.CreateService().RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        result.Error.Code.Should().Be("SignupPhone.DuplicateTrial");
        _support.Sms.Verify(s => s.SendOneTimeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestCode_Should_Fail_Clearly_When_Sms_Not_Configured()
    {
        _support.Sms.SetupGet(s => s.IsConfigured).Returns(false);

        var result = await _support.CreateService().RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        result.Error.Code.Should().Be("Sms.NotConfigured");
        _support.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestCode_Should_Not_Store_Row_When_Sending_Fails()
    {
        _support.Sms.Setup(s => s.SendOneTimeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SmsSendResult.Failure("netgsm-503-SERVICE_UNAVAILABLE"));

        var result = await _support.CreateService().RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        result.Error.Code.Should().Be("Sms.SendFailed");
        _support.Rows.Should().BeEmpty("başarısız gönderim bekleme ve sınırlardan düşmez");
    }

    [Fact]
    public async Task VerifyCode_Should_Count_Wrong_Attempt_And_Return_Token_On_Match()
    {
        var service = _support.CreateService();
        await service.RequestCodeAsync(Phone, null, Now, CancellationToken.None);

        var wrong = await service.VerifyCodeAsync(Phone, "654321", Now.AddMinutes(1), CancellationToken.None);
        wrong.Error.Code.Should().Be("SignupPhone.CodeInvalid");
        _support.Rows.Single().FailedAttempts.Should().Be(1);

        var ok = await service.VerifyCodeAsync(Phone, " 123456 ", Now.AddMinutes(2), CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.PhoneVerificationToken.Should().Be(SignupPhoneVerificationTestSupport.Token);
        ok.Value.ExpiresAtUtc.Should().Be(Now.AddMinutes(32));
        _support.Repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));

        (await service.FindForSignupAsync(Phone, SignupPhoneVerificationTestSupport.Token, Now.AddMinutes(10), CancellationToken.None))
            .IsSuccess.Should().BeTrue();
        (await service.FindForSignupAsync(Phone, "baska", Now.AddMinutes(10), CancellationToken.None))
            .Error.Code.Should().Be("SignupPhone.VerificationRequired");
    }

    [Fact]
    public async Task VerifyCode_Should_Return_CodeInvalid_When_No_Code_Was_Sent()
        => (await _support.CreateService().VerifyCodeAsync(Phone, "123456", Now, CancellationToken.None))
            .Error.Code.Should().Be("SignupPhone.CodeInvalid");
}
