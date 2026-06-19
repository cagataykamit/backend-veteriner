using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.Update;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.IntegrationEvents;

public sealed class ClientCommandHandlerOutboxEmissionTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IRepository<Client>> _clientsWrite = new();
    private readonly Mock<IClientIntegrationEventOutbox> _eventOutbox = new();

    private CreateClientCommandHandler CreateCreateHandler()
        => new(_tenantContext.Object, _tenantsRead.Object, _clientsRead.Object, _clientsWrite.Object, _eventOutbox.Object);

    private UpdateClientCommandHandler CreateUpdateHandler()
        => new(_tenantContext.Object, _tenantsRead.Object, _clientsRead.Object, _clientsWrite.Object, _eventOutbox.Object);

    private static Tenant ActiveTenant(Guid tid)
    {
        var tenant = new Tenant("Klinik A.Ş.");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        return tenant;
    }

    private void SetupActiveTenant(Guid tid)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveTenant(tid));
    }

    private void SetupNoDuplicates()
    {
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
    }

    [Fact]
    public async Task Create_Should_Emit_ClientCreated_With_CorrectPayload()
    {
        var tid = Guid.NewGuid();
        SetupActiveTenant(tid);
        SetupNoDuplicates();

        Client? captured = null;
        _clientsWrite.Setup(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .Callback<Client, CancellationToken>((c, _) => captured = c);

        ClientCreatedIntegrationEvent? evt = null;
        _eventOutbox
            .Setup(o => o.EnqueueAsync(
                ClientIntegrationEventTypes.Created,
                It.IsAny<ClientCreatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ClientCreatedIntegrationEvent, CancellationToken>((_, e, _) => evt = e)
            .Returns(Task.CompletedTask);

        var cmd = new CreateClientCommand("Ayşe Yılmaz", Email: "Ayse@Example.com", Phone: "05321234567", Address: "Ankara");
        var result = await CreateCreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();

        evt.Should().NotBeNull();
        evt!.EventId.Should().NotBeEmpty();
        evt.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var snap = evt.Current;
        snap.ClientId.Should().Be(captured!.Id);
        snap.TenantId.Should().Be(tid);
        snap.FullName.Should().Be("Ayşe Yılmaz");
        snap.FullNameNormalized.Should().Be(Client.NormalizeFullNameForDuplicateCheck("Ayşe Yılmaz"));
        snap.Email.Should().Be("ayse@example.com");
        snap.Phone.Should().Be("905321234567");
        snap.PhoneNormalized.Should().Be("905321234567");
        snap.CreatedAtUtc.Should().Be(captured.CreatedAtUtc);

        _eventOutbox.Verify(o => o.EnqueueAsync(
            ClientIntegrationEventTypes.Created,
            It.IsAny<ClientCreatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_NotEmit_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateCreateHandler().Handle(new CreateClientCommand("Ali Veli"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_NotEmit_When_DuplicateNameAndEmail()
    {
        var tid = Guid.NewGuid();
        SetupActiveTenant(tid);

        var existing = new Client(tid, "Ahmet Yılmaz", phone: null, email: "a@x.com");
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var cmd = new CreateClientCommand("Ahmet Yılmaz", Email: "a@x.com");
        var result = await CreateCreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Emit_ClientUpdated_With_CorrectPayload()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupActiveTenant(tid);
        SetupNoDuplicates();

        var existing = new Client(tid, "Eski Ad", "05321112233", "eski@example.com", "Eski adres");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(existing, cid);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        ClientUpdatedIntegrationEvent? evt = null;
        _eventOutbox
            .Setup(o => o.EnqueueAsync(
                ClientIntegrationEventTypes.Updated,
                It.IsAny<ClientUpdatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ClientUpdatedIntegrationEvent, CancellationToken>((_, e, _) => evt = e)
            .Returns(Task.CompletedTask);

        var cmd = new UpdateClientCommand(cid, "Ayşe Yılmaz", "Ayse@Example.com", "05321234567", "Ankara");
        var result = await CreateUpdateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        evt.Should().NotBeNull();
        evt!.EventId.Should().NotBeEmpty();
        evt.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var snap = evt.Current;
        snap.ClientId.Should().Be(cid);
        snap.TenantId.Should().Be(tid);
        snap.FullName.Should().Be("Ayşe Yılmaz");
        snap.FullNameNormalized.Should().Be(Client.NormalizeFullNameForDuplicateCheck("Ayşe Yılmaz"));
        snap.Email.Should().Be("ayse@example.com");
        snap.Phone.Should().Be("905321234567");
        snap.PhoneNormalized.Should().Be("905321234567");

        _eventOutbox.Verify(o => o.EnqueueAsync(
            ClientIntegrationEventTypes.Updated,
            It.IsAny<ClientUpdatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_NotEmit_When_ClientNotFound()
    {
        var tid = Guid.NewGuid();
        SetupActiveTenant(tid);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateUpdateHandler().Handle(new UpdateClientCommand(Guid.NewGuid(), "Ali Veli"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_NotEmit_When_DuplicateNamePhone_MatchesOtherClient()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        SetupActiveTenant(tid);

        var self = new Client(tid, "Eski Ad", "05329999999", "self@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(self, cid);
        var other = new Client(tid, "Ortak Ad", "05321112233", "other@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(other, otherId);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(self);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var cmd = new UpdateClientCommand(cid, "Ortak Ad", "yeni@example.com", "05321112233", null);
        var result = await CreateUpdateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
