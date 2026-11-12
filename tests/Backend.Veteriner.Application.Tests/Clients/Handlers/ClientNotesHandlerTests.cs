using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.Update;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

/// <summary>Müşteri notu (PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md §2.1): oluştur, güncelle (null = dokunma, "" = temizle), denetimde "***".</summary>
public sealed class ClientNotesHandlerTests
{
    private const string NoteText = "Deneme notu: akşam aranmak istiyor";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IRepository<Client>> _clientsWrite = new();
    private readonly Mock<IClientIntegrationEventOutbox> _outbox = new();
    private readonly Client _existing;

    public ClientNotesHandlerTests()
    {
        var tenant = new Tenant("Deneme Klinik", DateTime.UtcNow);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        _existing = new Client(_tenantId, "Deneme Müşteri", DateTime.UtcNow);
        _existing.ApplyNotes("Eski not");
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(_existing);
    }

    private UpdateClientCommandHandler UpdateHandler()
        => new(_tenantContext.Object, _tenants.Object, _clientsRead.Object, _clientsWrite.Object, _outbox.Object, TimeProvider.System);

    private CreateClientCommandHandler CreateHandler()
        => new(_tenantContext.Object, _tenants.Object, _clientsRead.Object, _clientsWrite.Object, _outbox.Object, TimeProvider.System, TrialQuotaGuardStub.Open);

    [Fact]
    public async Task Create_Should_Store_Trimmed_Note()
    {
        Client? added = null;
        _clientsWrite.Setup(w => w.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .Callback<Client, CancellationToken>((c, _) => added = c);

        var result = await CreateHandler().Handle(new CreateClientCommand("Yeni Müşteri", Notes: $"  {NoteText} "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.Notes.Should().Be(NoteText);
    }

    [Fact]
    public async Task Create_Should_Reject_Too_Long_Note()
    {
        var result = await CreateHandler().Handle(
            new CreateClientCommand("Yeni Müşteri", Notes: new string('x', Client.MaxNotesLength + 1)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.Validation");
        _clientsWrite.Verify(w => w.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Without_Note_Should_Not_Touch_It()
    {
        var result = await UpdateHandler().Handle(new UpdateClientCommand(_existing.Id, "Deneme Müşteri"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _existing.Notes.Should().Be("Eski not");
    }

    [Fact]
    public async Task Update_With_Empty_Note_Should_Clear_It()
    {
        var result = await UpdateHandler().Handle(new UpdateClientCommand(_existing.Id, "Deneme Müşteri", Notes: ""), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _existing.Notes.Should().BeNull();
    }

    [Fact]
    public async Task Update_Should_Replace_Note()
    {
        var result = await UpdateHandler().Handle(new UpdateClientCommand(_existing.Id, "Deneme Müşteri", Notes: NoteText), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _existing.Notes.Should().Be(NoteText);
    }

    [Theory]
    [InlineData(NoteText, "\"notes\":\"***\"")]
    [InlineData("", "\"notes\":\"\"")]
    [InlineData(null, "\"notes\":null")]
    public async Task Audit_Should_Write_Only_Whether_Note_Is_Filled(string? notes, string expected)
    {
        AuditLogEntry? entry = null;
        var writer = new Mock<IAuditLogWriter>();
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => entry = e)
            .Returns(Task.CompletedTask);
        var behavior = new AuditBehavior<UpdateClientCommand, Result>(
            new Mock<IClientContext>().Object, writer.Object, NullLogger<AuditBehavior<UpdateClientCommand, Result>>.Instance, TimeProvider.System);

        await behavior.Handle(
            new UpdateClientCommand(_existing.Id, "Deneme Müşteri", Notes: notes),
            (_, _) => ValueTask.FromResult(Result.Success()),
            CancellationToken.None);

        entry!.RequestPayload.Should().Contain(expected).And.NotContain("akşam aranmak");
    }
}
