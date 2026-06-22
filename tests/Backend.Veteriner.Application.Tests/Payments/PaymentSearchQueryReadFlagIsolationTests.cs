using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Payments;

/// <summary>
/// CQRS-15O: Payment list / report JSON / export Query DB flag'lerinin birbirinden bağımsız
/// routing davranışı (cross-flag interference yok).
/// </summary>
public sealed class PaymentSearchQueryReadFlagIsolationTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IPaymentsListReadModelReader> _listReader = new();
    private readonly Mock<IPaymentsReportReadModelReader> _reportReader = new();
    private readonly Mock<IPaymentsReportExportReadModelReader> _exportReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private static readonly DateTime From = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("pamuk")]
    public async Task PaymentList_Should_UseCommandDb_WhenOnlyReportAndExportFlagsEnabled(string? search)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyCommandListPath();
        if (search is not null)
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }

        var handler = new GetPaymentsListQueryHandler(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _pets.Object,
            _clients.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _listReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsListReadEnabled = false,
                PaymentsReportReadEnabled = true,
                PaymentsReportExportReadEnabled = true
            }));

        var result = await handler.Handle(
            new GetPaymentsListQuery(new PaymentListPagingRequest { Page = 1, PageSize = 20 }, Search: search),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _listReader.Verify(
            r => r.GetListAsync(It.IsAny<PaymentsListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pamuk")]
    public async Task JsonReport_Should_UseCommandDb_WhenOnlyListAndExportFlagsEnabled(string? search)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupCommandReportAggregates();
        if (search is not null)
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }

        var handler = new GetPaymentsReportQueryHandler(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _reportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsListReadEnabled = true,
                PaymentsReportReadEnabled = false,
                PaymentsReportExportReadEnabled = true
            }));

        var result = await handler.Handle(
            new GetPaymentsReportQuery(From, To, cid, null, null, null, search, 1, 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _reportReader.Verify(
            r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pamuk")]
    public async Task CsvExport_Should_UseCommandDb_WhenOnlyListAndReportFlagsEnabled(string? search)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        SetupCommandExportMocks();
        if (search is not null)
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }

        var handler = new ExportPaymentsReportQueryHandler(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _exportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsListReadEnabled = true,
                PaymentsReportReadEnabled = true,
                PaymentsReportExportReadEnabled = false
            }));

        var result = await handler.Handle(
            new ExportPaymentsReportQuery(From, To, cid, null, null, null, search),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pamuk")]
    public async Task XlsxExport_Should_UseCommandDb_WhenOnlyListAndReportFlagsEnabled(string? search)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
        SetupCommandExportMocks();
        if (search is not null)
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }

        var handler = new ExportPaymentsReportXlsxQueryHandler(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _exportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsListReadEnabled = true,
                PaymentsReportReadEnabled = true,
                PaymentsReportExportReadEnabled = false
            }));

        var result = await handler.Handle(
            new ExportPaymentsReportXlsxQuery(From, To, cid, null, null, null, search),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupEmptyCommandListPath()
    {
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsListFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentListRow>());
    }

    private void SetupCommandReportAggregates()
    {
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredAmountsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<decimal>());
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }

    private void SetupCommandExportMocks()
    {
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _payments.Setup(r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Payment>());
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _clinics.Setup(r => r.ListAsync(It.IsAny<ClinicsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());
    }
}
