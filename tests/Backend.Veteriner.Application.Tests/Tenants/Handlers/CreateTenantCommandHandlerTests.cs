using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.Create;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class CreateTenantCommandHandlerTests
{
    private readonly Mock<IReadRepository<Tenant>> _read = new();
    private readonly Mock<IRepository<Tenant>> _write = new();

    private CreateTenantCommandHandler CreateHandler() => new(_read.Object, _write.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_CaseInsensitive()
    {
        var handler = CreateHandler();
        var command = new CreateTenantCommand("Acme Vet");

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("acme vet"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.DuplicateName");

        _write.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Never);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateTenant_When_NameIsUnique()
    {
        var handler = CreateHandler();
        var command = new CreateTenantCommand("Yeni Klinik A.Ş.");

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        Tenant? captured = null;
        _write.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Callback<Tenant, CancellationToken>((t, _) => captured = t);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Yeni Klinik A.Ş.");
        captured.IsActive.Should().BeTrue();
        captured.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));

        _write.Verify(r => r.AddAsync(captured, It.IsAny<CancellationToken>()), Times.Once);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
