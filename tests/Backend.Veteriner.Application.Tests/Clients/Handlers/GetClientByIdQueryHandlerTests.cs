using Backend.Veteriner.Application.Clients.Queries.GetById;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

public sealed class GetClientByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClinicVeterinarianReader> _userNames = new();

    private GetClientByIdQueryHandler CreateHandler()
        => new(_tenantContext.Object, _clients.Object, _userNames.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var q = new GetClientByIdQuery(Guid.NewGuid());

        var result = await CreateHandler().Handle(q, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _clients.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClientNotFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(new GetClientByIdQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_ClientFound()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var created = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updated = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var entity = new Client(tid, "Ali Veli", "05321234567", "ali@example.com", "Ankara");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(entity, cid);
        typeof(Client).GetProperty(nameof(Client.CreatedAtUtc))!.SetValue(entity, created);
        typeof(Client).GetProperty(nameof(Client.UpdatedAtUtc))!.SetValue(entity, updated);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var result = await CreateHandler().Handle(new GetClientByIdQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Id.Should().Be(cid);
        dto.TenantId.Should().Be(tid);
        dto.CreatedAtUtc.Should().Be(created);
        dto.UpdatedAtUtc.Should().Be(updated);
        dto.FullName.Should().Be("Ali Veli");
        dto.Email.Should().Be("ali@example.com");
        dto.Phone.Should().Be("905321234567");
        dto.Address.Should().Be("Ankara");
        dto.KvkkConsentStatus.Should().Be(KvkkConsentStatus.None);
        dto.KvkkConsentRecordedOn.Should().BeNull();
        dto.KvkkConsentRecordedByName.Should().BeNull();
        dto.KvkkConsentTemplateVersion.Should().BeNull();
        dto.KvkkConsentTemplateHash.Should().BeNull();
        _userNames.Verify(
            r => r.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_KvkkConsent_With_Recorder_Name()
    {
        var tid = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entity = new Client(tid, "Ali Veli", "05321234567");
        entity.RecordKvkkConsent(KvkkConsentStatus.Accepted, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15), userId,
            4, ClinicKvkkFormTemplate.ComputeTextHash("Rıza metni"));
        typeof(Client).GetProperty(nameof(Client.RowVersion))!.SetValue(entity, new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 });

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _userNames.Setup(r => r.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string?> { [userId] = "Dr. Can" });

        var dto = (await CreateHandler().Handle(new GetClientByIdQuery(entity.Id), CancellationToken.None)).Value!;

        dto.KvkkConsentStatus.Should().Be(KvkkConsentStatus.Accepted);
        dto.KvkkConsentRecordedOn.Should().Be(new DateOnly(2026, 10, 1));
        dto.KvkkConsentRecordedByUserId.Should().Be(userId);
        dto.KvkkConsentRecordedByName.Should().Be("Dr. Can");
        dto.KvkkConsentTemplateVersion.Should().Be(4);
        dto.KvkkConsentTemplateHash.Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Rıza metni"));
        dto.RowVersion.Should().Be(Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 }));
    }
}
