using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-12D-9: PaymentsSearchLookupEnabled routing for CSV/XLSX export pipeline.</summary>
public sealed class ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IPaymentsReportExportReadModelReader> _exportReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CsvExport_SearchLookup_Should_RouteByPaymentsSearchFlag(bool paymentsSearchLookupEnabled)
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportAggregateMocks();
        SetupSearchMocks(paymentsSearchLookupEnabled);

        await CreateCsvHandler(paymentsSearchLookupEnabled).Handle(
            CreateExportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        AssertSearchRouting(paymentsSearchLookupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task XlsxExport_SearchLookup_Should_RouteByPaymentsSearchFlag(bool paymentsSearchLookupEnabled)
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportAggregateMocks();
        SetupSearchMocks(paymentsSearchLookupEnabled);

        await CreateXlsxHandler(paymentsSearchLookupEnabled).Handle(
            CreateXlsxExportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        AssertSearchRouting(paymentsSearchLookupEnabled);
    }

    [Fact]
    public async Task CsvExport_WhenCommandPath_AndSearchLookupFlagTrue_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportAggregateMocks();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateCsvHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateExportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CsvExport_WhenCommandPath_AndSearchWhitespaceOnly_Should_NotCallLookupReaders()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportAggregateMocks();

        await CreateCsvHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateExportQuery(clinicId, search: "   "),
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CsvExport_WhenExportReadFlagTrue_AndSearchProvided_Should_UseQueryLookup_RegardlessOfPaymentsSearchFlag()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        _exportReader
            .Setup(r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentsReportExportReadResult(0, Array.Empty<PaymentReportItemDto>()));
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));

        await CreateCsvHandler(
                paymentsSearchLookupEnabled: false,
                paymentsReportExportReadEnabled: true)
            .Handle(CreateExportQuery(clinicId, search: "pamuk"), CancellationToken.None);

        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CsvExport_WhenExportReadFlagTrue_AndLookupThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateCsvHandler(
                paymentsSearchLookupEnabled: true,
                paymentsReportExportReadEnabled: true)
            .Handle(CreateExportQuery(clinicId, search: "pamuk"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CsvExport_WhenExportReadFlagTrue_AndSearchWhitespaceOnly_Should_UseQueryReader_WithoutLookup()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        _exportReader
            .Setup(r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentsReportExportReadResult(0, Array.Empty<PaymentReportItemDto>()));

        await CreateCsvHandler(
                paymentsSearchLookupEnabled: true,
                paymentsReportExportReadEnabled: true)
            .Handle(CreateExportQuery(clinicId, search: "   "), CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CsvExport_WhenFlagTrue_Should_ResolveClientAndPetIdsSeparately()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportAggregateMocks();
        _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                It.IsAny<ClientTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientTextSearchLookupResult([clientId]));
        _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                It.IsAny<PetTextFieldsSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextFieldsSearchLookupResult([petId]));

        await CreateCsvHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateExportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CsvExport_WhenTooManyRows_Should_FailBeforeOrderedList_RegardlessOfFlag()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        _payments.Setup(r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentsReportConstants.MaxExportRows + 1);
        SetupSearchMocks(paymentsSearchLookupEnabled: true);

        var result = await CreateCsvHandler(paymentsSearchLookupEnabled: true).Handle(
            CreateExportQuery(clinicId, search: "pamuk"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupTenantAndClinic(Guid tid, Guid clinicId)
    {
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
    }

    private void SetupExportAggregateMocks()
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

    private void SetupSearchMocks(bool paymentsSearchLookupEnabled)
    {
        if (paymentsSearchLookupEnabled)
        {
            _clientLookupReader.Setup(r => r.ResolveClientIdsByTextSearchAsync(
                    It.IsAny<ClientTextSearchLookupRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ClientTextSearchLookupResult([]));
            _petLookupReader.Setup(r => r.ResolvePetIdsByPetTextFieldsAsync(
                    It.IsAny<PetTextFieldsSearchLookupRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PetTextFieldsSearchLookupResult([]));
        }
        else
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }
    }

    private void AssertSearchRouting(bool paymentsSearchLookupEnabled)
    {
        if (paymentsSearchLookupEnabled)
        {
            _clientLookupReader.Verify(
                r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _petLookupReader.Verify(
                r => r.ResolvePetIdsByPetTextFieldsAsync(It.IsAny<PetTextFieldsSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _clients.Verify(
                r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        else
        {
            _clientLookupReader.Verify(
                r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _clients.Verify(
                r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _pets.Verify(
                r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    private static ExportPaymentsReportQuery CreateExportQuery(Guid clinicId, string? search)
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;
        return new ExportPaymentsReportQuery(from, to, clinicId, null, null, null, search);
    }

    private static ExportPaymentsReportXlsxQuery CreateXlsxExportQuery(Guid clinicId, string? search)
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;
        return new ExportPaymentsReportXlsxQuery(from, to, clinicId, null, null, null, search);
    }

    private ExportPaymentsReportQueryHandler CreateCsvHandler(
        bool paymentsSearchLookupEnabled,
        bool paymentsReportExportReadEnabled = false)
        => new(
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
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled,
                PaymentsReportExportReadEnabled = paymentsReportExportReadEnabled
            }));

    private ExportPaymentsReportXlsxQueryHandler CreateXlsxHandler(
        bool paymentsSearchLookupEnabled,
        bool paymentsReportExportReadEnabled = false)
        => new(
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
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled,
                PaymentsReportExportReadEnabled = paymentsReportExportReadEnabled
            }));
}
