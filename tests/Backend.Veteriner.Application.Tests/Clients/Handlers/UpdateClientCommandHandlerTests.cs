using Backend.Veteriner.Application.Clients.Commands.Update;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class UpdateClientCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IRepository<Client>> _clientsWrite = new();
    private readonly Mock<IClientIntegrationEventOutbox> _eventOutbox = new();

    private UpdateClientCommandHandler CreateHandler()
        => new(_tenantContext.Object, _tenantsRead.Object, _clientsRead.Object, _clientsWrite.Object, _eventOutbox.Object, TimeProvider.System);

    private static void AlignTenantId(Tenant tenant, Guid id)
        => typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, id);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        var handler = CreateHandler();
        var cmd = new UpdateClientCommand(Guid.NewGuid(), "Ali Veli");

        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClientNotFound()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new UpdateClientCommand(Guid.NewGuid(), "Ali Veli");
        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Handle_Should_UpdateClient_And_SetUpdatedAtUtc_When_Valid()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var cmd = new UpdateClientCommand(cid, "Ayşe Yılmaz", "ayse@example.com", "05321234567", "Ankara");

        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);
        var existing = new Client(tid, "Eski Ad", DateTime.UtcNow, "05321112233", "eski@example.com", "Eski adres");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(existing, cid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var before = DateTime.UtcNow.AddSeconds(-1);
        var result = await handler.Handle(cmd, CancellationToken.None);
        var after = DateTime.UtcNow.AddSeconds(1);

        result.IsSuccess.Should().BeTrue();
        existing.FullName.Should().Be("Ayşe Yılmaz");
        existing.Email.Should().Be("ayse@example.com");
        existing.Phone.Should().Be("905321234567");
        existing.Address.Should().Be("Ankara");
        existing.UpdatedAtUtc.Should().NotBeNull();
        existing.UpdatedAtUtc!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Theory]
    [InlineData(null, null, ContactChannel.Sms, true)]
    [InlineData(ContactChannel.None, false, ContactChannel.None, false)]
    [InlineData(ContactChannel.WhatsApp, null, ContactChannel.WhatsApp, true)]
    public async Task Handle_Should_Apply_ContactPreferences_And_Keep_When_Omitted(
        ContactChannel? channel, bool? optedOut, ContactChannel expectedChannel, bool expectedOptedOut)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);
        var existing = new Client(tid, "Ayşe Yılmaz", DateTime.UtcNow, "05321112233", "ayse@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(existing, cid);
        existing.ApplyContactPreferences(ContactChannel.Sms, true);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateHandler().Handle(
            new UpdateClientCommand(cid, "Ayşe Yılmaz", "ayse@example.com", "05321112233", null, channel, optedOut),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.PreferredContactChannel.Should().Be(expectedChannel);
        existing.RemindersOptedOut.Should().Be(expectedOptedOut);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_DuplicateNameEmailOrNamePhoneQueryReturnsSameClient()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var cmd = new UpdateClientCommand(cid, "Ayşe Yılmaz", "ayse@example.com", "05321234567", "Ankara");

        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);
        var existing = new Client(tid, "Ayşe Yılmaz", DateTime.UtcNow, "05321234567", "ayse@example.com", "Ankara");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(existing, cid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateFullNameAndEmail_MatchesOtherClient()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var cmd = new UpdateClientCommand(cid, "Çakışan Ad", "dup@example.com", null, null);

        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);
        var self = new Client(tid, "Eski", DateTime.UtcNow, null, "old@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(self, cid);
        var other = new Client(tid, "Çakışan Ad", DateTime.UtcNow, null, "dup@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(other, otherId);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(self);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateFullNameAndPhone_MatchesOtherClient()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var cmd = new UpdateClientCommand(cid, "Ortak Ad", "yeni@example.com", "05321112233", null);

        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);
        var self = new Client(tid, "Eski Ad", DateTime.UtcNow, "05329999999", "self@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(self, cid);
        var other = new Client(tid, "Ortak Ad", DateTime.UtcNow, "05321112233", "other@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(other, otherId);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(self);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Succeed_When_NoDuplicateInTenant_FullNameEmailCheckReturnsNull()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new UpdateClientCommand(Guid.NewGuid(), "Yalnız Bu Tenant", "only@example.com", null, null);

        var tenant = new Tenant("Klinik A.Ş.", DateTime.UtcNow);
        AlignTenantId(tenant, tid);
        var self = new Client(tid, "Eski", DateTime.UtcNow, null, "only@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(self, cmd.Id);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(self);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
