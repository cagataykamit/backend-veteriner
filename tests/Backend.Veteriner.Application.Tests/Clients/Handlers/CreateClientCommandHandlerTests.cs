using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class CreateClientCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IRepository<Client>> _clientsWrite = new();

    private CreateClientCommandHandler CreateHandler()
        => new(_tenantContext.Object, _tenantsRead.Object, _clientsRead.Object, _clientsWrite.Object);

    private static void AlignTenantId(Tenant tenant, Guid id)
        => typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, id);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        var handler = CreateHandler();
        var cmd = new CreateClientCommand("Ali Veli", null);

        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantNotFound()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand("Ali Veli", null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantInactive()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand("Ali Veli", Phone: "05321111111");

        var tenant = new Tenant("A.Ş.");
        AlignTenantId(tenant, tid);
        tenant.Deactivate();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateEmailAndPhone()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand("Ayşe Yılmaz", Email: "Ayse@Example.com", Phone: "0532 123 45 67");

        var tenant = new Tenant("Klinik A.Ş.");
        AlignTenantId(tenant, tid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedEmailAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Önceki Kayıt", "05321234567", "ayse@example.com"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateClient_When_SamePhoneButDifferentEmail()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand("Yeni Kişi", Email: "baska@example.com", Phone: "05321234567");

        var tenant = new Tenant("Klinik A.Ş.");
        AlignTenantId(tenant, tid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedEmailAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_CreateClient_When_Valid()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand("Mehmet Demir", null);

        var tenant = new Tenant("Klinik A.Ş.");
        AlignTenantId(tenant, tid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantNormalizedEmailAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        Client? captured = null;
        _clientsWrite.Setup(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .Callback<Client, CancellationToken>((c, _) => captured = c);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var created = result.Value!;
        created.Id.Should().NotBeEmpty();
        created.FullName.Should().Be("Mehmet Demir");
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.FullName.Should().Be("Mehmet Demir");
        captured.Phone.Should().BeNull();

        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
