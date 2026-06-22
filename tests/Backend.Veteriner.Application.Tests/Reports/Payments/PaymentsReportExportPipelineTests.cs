using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-15I: Ortak export pipeline — 50k boundary, sıralama ve mapping guard.</summary>
public sealed class PaymentsReportExportPipelineTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Theory]
    [InlineData(49_999)]
    [InlineData(50_000)]
    public async Task LoadAsync_Should_AllowExport_When_CountAtOrBelowMaxExportRows(int totalRows)
    {
        SetupTenantAndClinic(Guid.NewGuid(), Guid.NewGuid());
        SetupMappingMocks();
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totalRows);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = await InvokePipeline(from, to, search: null);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadAsync_Should_Reject_When_CountExceedsMaxExportRows()
    {
        SetupTenantAndClinic(Guid.NewGuid(), Guid.NewGuid());
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentsReportConstants.MaxExportRows + 1);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        var result = await InvokePipeline(from, to, search: null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_WhenTooManyRows_Should_NotRunItemMapping()
    {
        SetupTenantAndClinic(Guid.NewGuid(), Guid.NewGuid());
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentsReportConstants.MaxExportRows + 1);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        await InvokePipeline(from, to, search: null);

        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clinics.Verify(
            r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_Should_RunValidationBeforeSearchAndCount()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var from = DateTime.UtcNow;
        var to = DateTime.UtcNow.AddDays(-1);

        await InvokePipeline(from, to, search: "pamuk");

        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_Should_RunSearchResolutionBeforeCount_WhenValidationPasses()
    {
        var callOrder = new List<string>();
        SetupTenantAndClinic(Guid.NewGuid(), Guid.NewGuid());
        SetupMappingMocks();

        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("search"))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("search"))
            .ReturnsAsync(new List<Pet>());
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("count"))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());

        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

        await InvokePipeline(from, to, search: "pamuk", paymentsSearchLookupEnabled: false);

        callOrder.Should().ContainInOrder("search", "count");
    }

    [Fact]
    public async Task LoadAsync_Should_RunCountValidationBeforeListAndMapping()
    {
        var callOrder = new List<string>();
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);

        var paidAt = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        var payment = new Payment(tid, clinicId, clientId, petId, null, null, 1m, "TRY", PaymentMethod.Cash, paidAt, null);

        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("count"))
            .ReturnsAsync(1);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("list"))
            .ReturnsAsync(new List<Payment> { payment });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("map-clients"))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("map-pets"))
            .ReturnsAsync(new List<Pet>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("map-clinics"))
            .ReturnsAsync(new List<Clinic>());

        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        await InvokePipeline(from, to, search: null);

        callOrder.Should().ContainInOrder("count", "list", "map-clients", "map-pets", "map-clinics");
    }

    private void SetupTenantAndClinic(Guid tid, Guid clinicId)
    {
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
    }

    private void SetupMappingMocks()
    {
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }

    private Task<Result<PaymentsReportExportPipeline.Loaded>> InvokePipeline(
        DateTime fromUtc,
        DateTime toUtc,
        string? search,
        bool paymentsSearchLookupEnabled = false)
        => PaymentsReportExportPipeline.LoadAsync(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            fromUtc,
            toUtc,
            clinicId: null,
            method: null,
            clientId: null,
            petId: null,
            search,
            paymentsSearchLookupEnabled,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            CancellationToken.None);
}
