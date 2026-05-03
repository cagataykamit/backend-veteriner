using Backend.Veteriner.Application.Clinics.Commands.Create;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Handlers;

public sealed class CreateClinicCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IRepository<Clinic>> _clinicsWrite = new();

    private CreateClinicCommandHandler CreateHandler()
        => new(_tenantContext.Object, _tenantsRead.Object, _clinicsRead.Object, _clinicsWrite.Object);

    private static void AlignTenantId(Tenant tenant, Guid id)
        => typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, id);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantNotFound()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var cmd = new CreateClinicCommand("Merkez", "İstanbul");

        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
        _clinicsWrite.Verify(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantInactive()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var cmd = new CreateClinicCommand("Merkez", "İstanbul");

        var tenant = new Tenant("Pasif A.Ş.");
        AlignTenantId(tenant, tenantId);
        tenant.Deactivate();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _clinicsWrite.Verify(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_UnderSameTenant()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var cmd = new CreateClinicCommand("Merkez Şube", "Ankara");

        var tenant = new Tenant("Aktif A.Ş.");
        AlignTenantId(tenant, tenantId);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clinicsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByTenantAndNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tenantId, "MERKEZ ŞUBE", "İzmir"));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.DuplicateName");
        _clinicsWrite.Verify(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateClinic_When_Valid()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var cmd = new CreateClinicCommand("Merkez", "İstanbul");

        var tenant = new Tenant("Aktif A.Ş.");
        AlignTenantId(tenant, tenantId);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clinicsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByTenantAndNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        Clinic? captured = null;
        _clinicsWrite.Setup(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()))
            .Callback<Clinic, CancellationToken>((c, _) => captured = c);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tenantId);
        captured.Name.Should().Be("Merkez");
        captured.City.Should().Be("İstanbul");
        captured.IsActive.Should().BeTrue();

        _clinicsWrite.Verify(r => r.AddAsync(captured, It.IsAny<CancellationToken>()), Times.Once);
        _clinicsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_PersistProfileFields_When_Provided()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var cmd = new CreateClinicCommand(
            "Merkez",
            "İstanbul",
            Phone: "+90 555 000 00 00",
            Email: "info@vet.test",
            Address: "Cadde 1",
            Description: "Açıklama");

        var tenant = new Tenant("Aktif A.Ş.");
        AlignTenantId(tenant, tenantId);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _clinicsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByTenantAndNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        Clinic? captured = null;
        _clinicsWrite.Setup(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()))
            .Callback<Clinic, CancellationToken>((c, _) => captured = c);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Phone.Should().Be("+90 555 000 00 00");
        captured.Email.Should().Be("info@vet.test");
        captured.Address.Should().Be("Cadde 1");
        captured.Description.Should().Be("Açıklama");
    }
}
