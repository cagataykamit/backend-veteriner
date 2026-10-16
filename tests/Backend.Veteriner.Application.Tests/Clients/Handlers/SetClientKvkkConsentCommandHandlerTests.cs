using Backend.Veteriner.Application.Clients.Commands.SetKvkkConsent;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class SetClientKvkkConsentCommandHandlerTests
{
    private static readonly string ValidRowVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

    // 2026-10-14 21:30 UTC = İstanbul 2026-10-15 00:30; "bugün" İstanbul günüdür.
    private static readonly DateTime NowUtc = new(2026, 10, 14, 21, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly IstanbulToday = new(2026, 10, 15);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IRepository<Client>> _clients = new();
    private readonly Mock<IClinicVeterinarianReader> _userNames = new();

    public SetClientKvkkConsentCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _userNames.Setup(r => r.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string?> { [_userId] = "Sekreter Ayşe" });
    }

    private SetClientKvkkConsentCommandHandler CreateHandler()
        => new(_tenantContext.Object, _clientContext.Object, _tenants.Object, _clients.Object, _userNames.Object, new FixedClock(NowUtc));

    private Client ReturnClient(Client? client = null)
    {
        client ??= new Client(_tenantId, "Ali Veli", "905551112233");
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        return client;
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(Guid.NewGuid(), KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Client_NotFound()
    {
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(Guid.NewGuid(), KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Reject_WalkIn_Client()
    {
        var walkIn = ReturnClient(Client.CreateWalkIn(_tenantId));

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(walkIn.Id, KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Clients.WalkInReadOnly");
        _clients.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RowVersion_Missing()
    {
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Accepted, null, null), CancellationToken.None);

        result.Error.Code.Should().Be("Clients.Validation");
        client.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        _clients.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Record_Accepted_With_Istanbul_Today_And_Recorder()
    {
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KvkkConsentStatus.Should().Be(KvkkConsentStatus.Accepted);
        result.Value.KvkkConsentRecordedOn.Should().Be(IstanbulToday);
        result.Value.KvkkConsentRecordedByUserId.Should().Be(_userId);
        result.Value.KvkkConsentRecordedByName.Should().Be("Sekreter Ayşe");
        _clients.Verify(r => r.UpdateAsync(client, It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Reject_Future_Date()
    {
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Declined, IstanbulToday.AddDays(1), ValidRowVersion),
            CancellationToken.None);

        result.Error.Code.Should().Be("Clients.Validation");
        _clients.Verify(r => r.UpdateAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Declined_Should_Opt_Out_Of_Reminders_And_Return_Flag()
    {
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Declined, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RemindersOptedOut.Should().BeTrue();
        client.RemindersOptedOut.Should().BeTrue();
        new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Declined, null, ValidRowVersion)
            .AuditAction.Should().Be("Client.KvkkConsent.Declined");
        _clients.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Accepted_After_Declined_Should_Keep_Reminder_OptOut()
    {
        var client = ReturnClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, IstanbulToday, _userId);

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RemindersOptedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Revert_To_None_Without_Touching_Reminders()
    {
        var client = ReturnClient();
        client.ApplyContactPreferences(null, true);
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, IstanbulToday, _userId);

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.None, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        result.Value.KvkkConsentRecordedOn.Should().BeNull();
        result.Value.KvkkConsentRecordedByName.Should().BeNull();
        client.RemindersOptedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Map_Concurrency_To_Conflict()
    {
        var client = ReturnClient();
        _clients.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.ConcurrencyConflict");
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
