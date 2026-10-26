using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.DemoRequests.Commands.Create;
using Backend.Veteriner.Domain.DemoRequests;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.DemoRequests;

/// <summary>TRIAL-005: doğrulama kuralları, tuzak alan, bildirim adresi boş/dolu davranışı. Bkz. docs/TRIAL_DEMO_REQUEST_API_CONTRACT.md.</summary>
public sealed class CreateDemoRequestCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 30, 0, DateTimeKind.Utc);

    private readonly Mock<IRepository<DemoRequest>> _repo = new();
    private readonly Mock<IEmailSender> _email = new();

    private static CreateDemoRequestCommand Valid(string? website = null) => new(
        " Ayşe Yılmaz ", "Işık Veteriner", "İzmir", "ayse@example.com", null, DemoClinicSize.SmallTeam,
        "Excel", "Öğleden sonra uygunuz.", PrivacyNoticeAccepted: true, website);

    private CreateDemoRequestCommandHandler Handler(string? notifyEmail)
        => new(_repo.Object, _email.Object, Options.Create(new DemoRequestOptions { NotifyEmail = notifyEmail }),
            new FixedClock(Now), NullLogger<CreateDemoRequestCommandHandler>.Instance);

    [Fact]
    public void Validator_Should_Accept_Valid_Command()
        => new CreateDemoRequestCommandValidator().Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Validator_Should_Report_Contract_Field_Names()
    {
        var command = new CreateDemoRequestCommand(" a ", "", null, "gecersiz", new string('1', 31), null, null,
            new string('m', DemoRequest.MaxMessageLength + 1), PrivacyNoticeAccepted: false, Website: null);

        var errors = new CreateDemoRequestCommandValidator().Validate(command).Errors.Select(e => e.PropertyName).Distinct();

        errors.Should().BeEquivalentTo("contactName", "clinicName", "email", "phone", "clinicSize", "message", "privacyNoticeAccepted");
    }

    [Fact]
    public void Validator_Should_Skip_Rules_When_Honeypot_Filled()
    {
        var command = new CreateDemoRequestCommand(null, null, null, null, null, null, null, null, false, "http://spam");

        new CreateDemoRequestCommandValidator().Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Ignore_Honeypot_Without_Saving_Or_Mailing()
    {
        var result = await Handler("satis@example.com").Handle(Valid(website: "http://spam"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.AddAsync(It.IsAny<DemoRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _email.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Handle_Should_Save_Without_Mail_When_Notify_Email_Empty(string? notifyEmail)
    {
        DemoRequest? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<DemoRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DemoRequest, CancellationToken>((d, _) => saved = d)
            .ReturnsAsync((DemoRequest d, CancellationToken _) => d);

        var result = await Handler(notifyEmail).Handle(Valid(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved!.ContactName.Should().Be("Ayşe Yılmaz");
        saved.CreatedAtUtc.Should().Be(Now);
        saved.NotificationQueuedAtUtc.Should().BeNull();
        _email.VerifyNoOtherCalls();
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Queue_Notification_And_Mark_When_Notify_Email_Set()
    {
        DemoRequest? saved = null;
        string? subject = null, body = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<DemoRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DemoRequest, CancellationToken>((d, _) => saved = d)
            .ReturnsAsync((DemoRequest d, CancellationToken _) => d);
        _email.Setup(e => e.SendAsync("satis@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), false))
            .Callback<string, string, string, CancellationToken, bool>((_, s, b, _, _) => { subject = s; body = b; })
            .Returns(Task.CompletedTask);

        await Handler(" satis@example.com ").Handle(Valid(), CancellationToken.None);

        subject.Should().Be("Vetinity demo talebi — Işık Veteriner");
        body.Should().Contain("Ad soyad: Ayşe Yılmaz").And.Contain("E-posta: ayse@example.com")
            .And.Contain("Klinik büyüklüğü: 2–5 hekim").And.Contain("Telefon: -").And.Contain(saved!.Id.ToString());
        saved.NotificationQueuedAtUtc.Should().Be(Now);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
