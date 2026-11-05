using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Notifications;
using Backend.Veteriner.Domain.OwnerPortal;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Notifications;

/// <summary>MOBILE-003: dağıtıcının push kanalı ve outbox'tan cihazlara teslim (tercih yeniden okunur, geçersiz jeton silinir).</summary>
public sealed class OwnerPushDeliveryTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IReadRepository<OwnerAccount>> _accounts = new();
    private readonly Mock<IRepository<OwnerPushDevice>> _devices = new();
    private readonly Mock<IPushNotificationProvider> _provider = new();
    private readonly List<OwnerPushDevice> _deleted = [];

    public OwnerPushDeliveryTests()
    {
        _provider.SetupGet(p => p.IsConfigured).Returns(true);
        _devices.Setup(d => d.DeleteRangeAsync(It.IsAny<IEnumerable<OwnerPushDevice>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<OwnerPushDevice>, CancellationToken>((rows, _) => _deleted.AddRange(rows))
            .ReturnsAsync(0);
    }

    private OwnerAccount GivenAccount(bool pushEnabled = true)
    {
        var account = OwnerAccount.Create("a@b.com", "Ayşe", Now);
        account.SetPushEnabled(pushEnabled);
        _accounts.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<OwnerAccount>>(), It.IsAny<CancellationToken>())).ReturnsAsync(account);
        return account;
    }

    private void GivenDevices(params OwnerPushDevice[] devices)
        => _devices.Setup(r => r.ListAsync(It.IsAny<ISpecification<OwnerPushDevice>>(), It.IsAny<CancellationToken>())).ReturnsAsync(devices.ToList());

    private OwnerPushDevice Device(OwnerAccount account, string token, PushDeliveryOutcome outcome)
    {
        var device = OwnerPushDevice.Create(account.Id, OwnerPushPlatform.Android, token, Now);
        _provider.Setup(p => p.SendAsync(It.IsAny<OwnerPushPlatform>(), token, It.IsAny<PushMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        return device;
    }

    private OwnerPushDelivery Delivery() => new(_accounts.Object, _devices.Object, _provider.Object, NullLogger<OwnerPushDelivery>.Instance);

    private static OwnerPushOutboxPayload Payload(Guid accountId)
        => new() { OwnerAccountId = accountId, Type = OwnerNotificationType.DataRequestAnswered, Title = "Başlık", Body = "Metin" };

    [Fact]
    public async Task Invalid_Tokens_Are_Removed_And_Partial_Success_Is_Not_Retried()
    {
        var account = GivenAccount();
        var good = Device(account, "good", PushDeliveryOutcome.Delivered);
        var invalid = Device(account, "invalid", PushDeliveryOutcome.InvalidToken);
        var transient = Device(account, "transient", PushDeliveryOutcome.Failed);
        GivenDevices(good, invalid, transient);

        await Delivery().DeliverAsync(Payload(account.Id), CancellationToken.None);

        _deleted.Should().BeEquivalentTo([invalid]);
        _devices.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _provider.Verify(p => p.SendAsync(It.IsAny<OwnerPushPlatform>(), It.IsAny<string>(),
            It.Is<PushMessage>(m => m.Type == OwnerNotificationType.DataRequestAnswered && m.Title == "Başlık"), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task No_Device_Reached_With_Transient_Failure_Throws_For_Outbox_Retry()
    {
        var account = GivenAccount();
        var invalid = Device(account, "invalid", PushDeliveryOutcome.InvalidToken);
        var transient = Device(account, "transient", PushDeliveryOutcome.Failed);
        GivenDevices(invalid, transient);

        var act = () => Delivery().DeliverAsync(Payload(account.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _deleted.Should().BeEquivalentTo([invalid], "geçersiz jeton yeniden denemeden önce silinir");
    }

    [Fact]
    public async Task Preference_Turned_Off_Or_Provider_Missing_Sends_Nothing()
    {
        var account = GivenAccount(pushEnabled: false);
        await Delivery().DeliverAsync(Payload(account.Id), CancellationToken.None);

        _provider.SetupGet(p => p.IsConfigured).Returns(false);
        GivenAccount(pushEnabled: true);
        await Delivery().DeliverAsync(Payload(account.Id), CancellationToken.None);

        _provider.Verify(p => p.SendAsync(It.IsAny<OwnerPushPlatform>(), It.IsAny<string>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _devices.Verify(d => d.ListAsync(It.IsAny<ISpecification<OwnerPushDevice>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OwnerNotificationType.DataRequestAnswered, true, true, true)]
    [InlineData(OwnerNotificationType.DataRequestAnswered, false, true, false)]
    [InlineData(OwnerNotificationType.SignInCode, true, true, false)]
    [InlineData(OwnerNotificationType.VaccinationReminder, true, false, false)]
    [InlineData(OwnerNotificationType.VaccinationReminder, true, true, true)]
    public async Task Dispatcher_Queues_Push_By_Policy_And_Preference(OwnerNotificationType type, bool pushEnabled, bool optedIn, bool expectedQueued)
    {
        var push = new Mock<IPushNotificationSender>();
        push.SetupGet(p => p.IsConfigured).Returns(true);
        push.Setup(p => p.SendAsync(It.IsAny<OwnerNotificationType>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dispatcher = new OwnerNotificationDispatcher(new Mock<IEmailSender>().Object, push.Object, NullLogger<OwnerNotificationDispatcher>.Instance);
        var accountId = Guid.NewGuid();

        var result = await dispatcher.DispatchAsync(
            new OwnerNotificationMessage(type, accountId, "a@b.com", pushEnabled, optedIn, "Konu", "Metin", "Push başlık", "Push metin"),
            CancellationToken.None);

        result.PushQueued.Should().Be(expectedQueued);
        push.Verify(p => p.SendAsync(type, accountId, "Push başlık", "Push metin", It.IsAny<CancellationToken>()), expectedQueued ? Times.Once() : Times.Never());
    }
}
