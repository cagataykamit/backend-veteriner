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
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IRepository<Client>> _clientsWrite = new();

    private CreateClientCommandHandler CreateHandler()
        => new(_tenantsRead.Object, _clientsRead.Object, _clientsWrite.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantNotFound()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand(tid, "Ali Veli", null);

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
        var cmd = new CreateClientCommand(tid, "Ali Veli", "555");

        var tenant = new Tenant("A.Ş.");
        tenant.Deactivate();

        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateFullNameAndPhone()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand(tid, "Ayşe Yılmaz", "05321234567");

        var tenant = new Tenant("Klinik A.Ş.");

        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "ayşe yılmaz", "05321234567"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.DuplicateClient");
        _clientsWrite.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateClient_When_Valid()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new CreateClientCommand(tid, "Mehmet Demir", null);

        var tenant = new Tenant("Klinik A.Ş.");

        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByTenantFullNameAndPhoneSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        Client? captured = null;
        _clientsWrite.Setup(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .Callback<Client, CancellationToken>((c, _) => captured = c);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.FullName.Should().Be("Mehmet Demir");
        captured.Phone.Should().BeNull();

        _clientsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
