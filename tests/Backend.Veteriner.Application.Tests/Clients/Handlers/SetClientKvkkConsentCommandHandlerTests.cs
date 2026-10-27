using Backend.Veteriner.Application.Clients.Commands.SetKvkkConsent;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.KvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
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
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<ClinicKvkkFormTemplate>> _templates = new();
    private readonly Mock<IClinicVeterinarianReader> _userNames = new();

    public SetClientKvkkConsentCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A", DateTime.UtcNow));
        _userNames.Setup(r => r.GetNamesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string?> { [_userId] = "Sekreter Ayşe" });
    }

    private SetClientKvkkConsentCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _tenants.Object,
            _clients.Object,
            _clinicContext.Object,
            _clinics.Object,
            _templates.Object,
            _userNames.Object,
            new FixedClock(NowUtc));

    private Client ReturnClient(Client? client = null)
    {
        client ??= new Client(_tenantId, "Ali Veli", DateTime.UtcNow, "905551112233");
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
        var walkIn = ReturnClient(Client.CreateWalkIn(_tenantId, DateTime.UtcNow));

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
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, IstanbulToday, _userId, null, null, DateTime.UtcNow);

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
        client.RecordKvkkConsent(KvkkConsentStatus.Declined, null, IstanbulToday, _userId, null, null, DateTime.UtcNow);

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

    private Guid SetupClinic()
    {
        var clinic = new Clinic(_tenantId, "Merkez", "Izmir");
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinic.Id);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        return clinic.Id;
    }

    [Fact]
    public async Task Handle_Should_Record_Saved_Consent_Template_Version_And_Hash()
    {
        var clinicId = SetupClinic();
        var template = ClinicKvkkFormTemplate.Create(_tenantId, clinicId, ClinicKvkkFormKind.Consent, "Metin 1", _userId, DateTime.UtcNow).Value!;
        template.UpdateText("Metin 2", _userId, DateTime.UtcNow);
        _templates.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicKvkkFormTemplateByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KvkkConsentTemplateVersion.Should().Be(2);
        result.Value.KvkkConsentTemplateHash.Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Metin 2"));
        client.KvkkConsentTemplateVersion.Should().Be(2);
    }

    [Fact]
    public async Task Handle_Should_Record_Default_Text_As_Version_Zero()
    {
        SetupClinic();
        _templates.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicKvkkFormTemplateByClinicSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicKvkkFormTemplate?)null);
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Declined, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KvkkConsentTemplateVersion.Should().Be(ClinicKvkkFormTemplate.DefaultTextVersion);
        result.Value.KvkkConsentTemplateHash.Should().Be(
            ClinicKvkkFormTemplate.ComputeTextHash(ClinicKvkkFormTemplateDefaults.TextFor(ClinicKvkkFormKind.Consent)));
    }

    [Fact]
    public async Task Handle_Should_Leave_Version_Unknown_Without_Clinic_Context()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var client = ReturnClient();

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.Accepted, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KvkkConsentTemplateVersion.Should().BeNull();
        result.Value.KvkkConsentTemplateHash.Should().BeNull();
        _templates.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ClinicKvkkFormTemplateByClinicSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Revert_To_None_Should_Clear_Version_Without_Reading_Template()
    {
        SetupClinic();
        var client = ReturnClient();
        client.RecordKvkkConsent(KvkkConsentStatus.Accepted, null, IstanbulToday, _userId, 1, ClinicKvkkFormTemplate.ComputeTextHash("a"), DateTime.UtcNow);

        var result = await CreateHandler().Handle(
            new SetClientKvkkConsentCommand(client.Id, KvkkConsentStatus.None, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.KvkkConsentTemplateVersion.Should().BeNull();
        result.Value.KvkkConsentTemplateHash.Should().BeNull();
        _templates.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ClinicKvkkFormTemplateByClinicSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
