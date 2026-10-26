using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.Create.Validators;
using Backend.Veteriner.Application.Clients.Commands.Update;
using Backend.Veteriner.Application.Clients.Commands.Update.Validators;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Clients.Queries.GetInvoicingIdentity;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Common.Behaviors;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clients.Handlers;

/// <summary>
/// ADR-036 Dilim 1: müşteri fatura kimliği — oluştur/güncelle (null = dokunma), tam değer okuma izni, maskeli görünüm,
/// alan düzeyi doğrulama ve denetim kaydında maskeleme.
/// </summary>
public sealed class ClientInvoicingIdentityHandlerTests
{
    private const string Tckn = "10000000146";
    private const string Vkn = "1234567890";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IRepository<Client>> _clientsWrite = new();
    private readonly Mock<IClientIntegrationEventOutbox> _outbox = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Client _existing;

    public ClientInvoicingIdentityHandlerTests()
    {
        var tenant = new Tenant("Klinik A.Ş.");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        _existing = new Client(_tenantId, "Ayşe Yılmaz");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(_existing, _clientId);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(_existing);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Clients.Update)).Returns(true);
    }

    private static ClientInvoicingIdentityDto Individual(string? tckn = Tckn)
        => new(ClientPersonType.Individual, tckn, null, null, null, "İstanbul", "Kadıköy", "Moda Cad. 1");

    private UpdateClientCommandHandler UpdateHandler()
        => new(_tenantContext.Object, _tenants.Object, _clientsRead.Object, _clientsWrite.Object, _outbox.Object);

    private CreateClientCommandHandler CreateHandler()
        => new(_tenantContext.Object, _tenants.Object, _clientsRead.Object, _clientsWrite.Object, _outbox.Object);

    [Fact]
    public async Task Create_Should_Apply_Invoicing_Identity()
    {
        Client? added = null;
        _clientsWrite.Setup(w => w.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()))
            .Callback<Client, CancellationToken>((c, _) => added = c);

        var result = await CreateHandler().Handle(
            new CreateClientCommand("Mehmet Kaya", InvoicingIdentity: Individual()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added!.PersonType.Should().Be(ClientPersonType.Individual);
        added.NationalId.Should().Be(Tckn);
        added.BillingDistrict.Should().Be("Kadıköy");
    }

    [Fact]
    public async Task Create_Should_Fail_Without_Saving_When_Identity_Inconsistent()
    {
        var invalid = new ClientInvoicingIdentityDto(ClientPersonType.Individual, null, Vkn, null, null, null, null, null);

        var result = await CreateHandler().Handle(
            new CreateClientCommand("Mehmet Kaya", InvoicingIdentity: invalid), CancellationToken.None);

        result.Error.Code.Should().Be("Clients.Validation");
        _clientsWrite.Verify(w => w.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Replace_Identity_When_Given()
    {
        _existing.ApplyInvoicingIdentity(ClientPersonType.Individual, Tckn, null, null, null, "İzmir", null, null);
        var corporate = new ClientInvoicingIdentityDto(ClientPersonType.Corporate, null, Vkn, "Kadıköy VD", "Örnek Ltd.", null, null, null);

        var result = await UpdateHandler().Handle(
            new UpdateClientCommand(_clientId, "Ayşe Yılmaz", InvoicingIdentity: corporate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _existing.PersonType.Should().Be(ClientPersonType.Corporate);
        _existing.NationalId.Should().BeNull();
        _existing.TaxNumber.Should().Be(Vkn);
        _existing.BillingProvince.Should().BeNull();
    }

    [Fact]
    public async Task Update_Without_Identity_Should_Keep_Existing_Values()
    {
        _existing.ApplyInvoicingIdentity(ClientPersonType.Individual, Tckn, null, null, null, "İzmir", null, null);

        var result = await UpdateHandler().Handle(new UpdateClientCommand(_clientId, "Ayşe Yılmaz"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _existing.NationalId.Should().Be(Tckn);
        _existing.BillingProvince.Should().Be("İzmir");
    }

    [Fact]
    public async Task Full_Identity_Query_Should_Require_Clients_Update()
    {
        _existing.ApplyInvoicingIdentity(ClientPersonType.Individual, Tckn, null, null, null, null, null, null);
        _permissions.Setup(p => p.HasPermission(PermissionCatalog.Clients.Update)).Returns(false);
        var handler = new GetClientInvoicingIdentityQueryHandler(_tenantContext.Object, _permissions.Object, _clientsRead.Object);

        var result = await handler.Handle(new GetClientInvoicingIdentityQuery(_clientId), CancellationToken.None);

        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Full_Identity_Query_Should_Return_Unmasked_Values_For_Editor()
    {
        _existing.ApplyInvoicingIdentity(ClientPersonType.Individual, Tckn, null, null, null, "İzmir", null, null);
        var handler = new GetClientInvoicingIdentityQueryHandler(_tenantContext.Object, _permissions.Object, _clientsRead.Object);

        var result = await handler.Handle(new GetClientInvoicingIdentityQuery(_clientId), CancellationToken.None);

        result.Value!.NationalId.Should().Be(Tckn);
        result.Value.BillingProvince.Should().Be("İzmir");
    }

    [Fact]
    public void Summary_Should_Mask_Identity_Numbers()
    {
        _existing.ApplyInvoicingIdentity(ClientPersonType.Individual, Tckn, null, null, null, null, null, null);

        var summary = ClientInvoicingSummaryDto.From(_existing);

        summary.NationalIdMasked.Should().Be("100******46");
        summary.TaxNumberMasked.Should().BeNull();
    }

    [Fact]
    public void Validators_Should_Report_Field_Path_For_Invalid_Numbers()
    {
        var create = new CreateClientCommandValidator().Validate(
            new CreateClientCommand("Mehmet Kaya", InvoicingIdentity: Individual("12345678901")));
        var update = new UpdateClientCommandValidator().Validate(new UpdateClientCommand(
            _clientId, "Mehmet Kaya",
            InvoicingIdentity: new ClientInvoicingIdentityDto(ClientPersonType.Corporate, null, "1234567891", null, null, null, null, null)));

        create.Errors.Should().ContainSingle(e => e.PropertyName == "InvoicingIdentity.NationalId")
            .Which.ErrorMessage.Should().Be("TCKN 11 haneli ve geçerli olmalıdır.");
        update.Errors.Should().ContainSingle(e => e.PropertyName == "InvoicingIdentity.TaxNumber")
            .Which.ErrorMessage.Should().Be("Vergi numarası 10 haneli geçerli VKN veya 11 haneli geçerli TCKN olmalıdır.");
    }

    [Theory]
    [InlineData(Vkn, true)]
    [InlineData(Tckn, true)] // şahıs şirketi: vergi numarası olarak TCKN
    [InlineData("12345678901", false)]
    public void Validator_Should_Accept_Vkn_Or_Tckn_As_Corporate_Tax_Number(string taxNumber, bool valid)
        => new UpdateClientCommandValidator().Validate(new UpdateClientCommand(
                _clientId, "Mehmet Kaya",
                InvoicingIdentity: new ClientInvoicingIdentityDto(ClientPersonType.Corporate, null, taxNumber, null, null, null, null, null)))
            .Errors.Any(e => e.PropertyName == "InvoicingIdentity.TaxNumber").Should().Be(!valid);

    [Fact]
    public async Task Update_Should_Store_Tckn_As_Corporate_Tax_Number_And_Mask_It()
    {
        var soleProprietor = new ClientInvoicingIdentityDto(ClientPersonType.Corporate, null, Tckn, "Kadıköy VD", "Ayşe Yılmaz", null, null, null);

        var result = await UpdateHandler().Handle(
            new UpdateClientCommand(_clientId, "Ayşe Yılmaz", InvoicingIdentity: soleProprietor), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _existing.TaxNumber.Should().Be(Tckn);
        _existing.NationalId.Should().BeNull();
        ClientInvoicingSummaryDto.From(_existing).TaxNumberMasked.Should().Be("100******46");
    }

    [Fact]
    public async Task Audit_Should_Mask_Identity_Numbers_In_Request_Payload()
    {
        AuditLogEntry? entry = null;
        var writer = new Mock<IAuditLogWriter>();
        writer.Setup(w => w.WriteAsync(It.IsAny<AuditLogEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLogEntry, CancellationToken>((e, _) => entry = e)
            .Returns(Task.CompletedTask);
        var behavior = new AuditBehavior<UpdateClientCommand, Result>(
            new Mock<IClientContext>().Object, writer.Object, NullLogger<AuditBehavior<UpdateClientCommand, Result>>.Instance);
        var command = new UpdateClientCommand(
            _clientId, "Ayşe Yılmaz",
            InvoicingIdentity: new ClientInvoicingIdentityDto(ClientPersonType.Corporate, null, Vkn, "VD", null, null, null, null));

        await behavior.Handle(command, (_, _) => ValueTask.FromResult(Result.Success()), CancellationToken.None);

        entry!.Action.Should().Be("Client.Update");
        entry.RequestPayload.Should().Contain("\"taxNumber\":\"123*****90\"").And.NotContain(Vkn);
    }
}
