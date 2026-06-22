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

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>
/// CQRS-15J: <see cref="QueryReadModelsOptions.PaymentsReportExportReadEnabled"/> routing for payment export CSV/XLSX.
/// </summary>
public sealed class PaymentsReportExportReadRoutingTests
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
    private readonly Mock<IPaymentsReportReadModelReader> _jsonReportReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private static readonly DateTime From = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

    [Fact]
    public async Task WhenExportFlagFalse_Csv_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupCommandExportMocks();

        var result = await CreateCsvHandler(exportReadEnabled: false)
            .Handle(CsvQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyCommandExportPathUsed();
        VerifyExportReaderNeverCalled();
    }

    [Fact]
    public async Task WhenExportFlagFalse_Xlsx_Should_UseCommandDb_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupCommandExportMocks();

        var result = await CreateXlsxHandler(exportReadEnabled: false)
            .Handle(XlsxQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyCommandExportPathUsed();
        VerifyExportReaderNeverCalled();
    }

    [Fact]
    public async Task WhenExportFlagTrue_AndSearchEmpty_AndActiveClinic_Csv_Should_UseQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(EmptyExportResult());

        await CreateCsvHandler(exportReadEnabled: true).Handle(CsvQuery(cid), CancellationToken.None);

        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenExportFlagTrue_AndSearchEmpty_AndActiveClinic_Xlsx_Should_UseQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(EmptyExportResult());

        await CreateXlsxHandler(exportReadEnabled: true).Handle(XlsxQuery(cid), CancellationToken.None);

        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenExportFlagTrue_AndSearchEmpty_AndTenantWide_Should_UseQueryReader_WithoutClinicFilter()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        PaymentsReportExportReadRequest? captured = null;
        _exportReader
            .Setup(r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentsReportExportReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(EmptyExportResult());

        await CreateCsvHandler(exportReadEnabled: true).Handle(CsvQuery(null), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ClinicId.Should().BeNull();
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenExportFlagTrue_AndSearchPresent_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupCommandExportMocks();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var result = await CreateCsvHandler(exportReadEnabled: true)
            .Handle(CsvQuery(cid, search: "pamuk"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyExportReaderNeverCalled();
        VerifyCommandExportPathUsed();
    }

    [Fact]
    public async Task WhenExportFlagTrue_AndSearchWhitespace_Should_UseQueryReader()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(EmptyExportResult());

        await CreateCsvHandler(exportReadEnabled: true)
            .Handle(CsvQuery(cid, search: "   "), CancellationToken.None);

        _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenExportFlagTrue_AndMultiClinicScope_Should_FallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        SetupCommandExportMocks();

        var result = await CreateCsvHandler(exportReadEnabled: true, scopeResolver: scopeResolver)
            .Handle(CsvQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyExportReaderNeverCalled();
        VerifyCommandExportPathUsed();
    }

    [Fact]
    public async Task WhenQueryPath_AndQueryDbEmpty_Should_ReturnEmptyExport_WithoutCommandFallback()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(EmptyExportResult());

        var result = await CreateCsvHandler(exportReadEnabled: true)
            .Handle(CsvQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        _exportReader
            .Setup(r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateCsvHandler(exportReadEnabled: true).Handle(CsvQuery(cid), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyCommandExportPathNeverUsed();
    }

    [Theory]
    [InlineData(49_999)]
    [InlineData(50_000)]
    public async Task WhenQueryPath_AndCountAtOrBelowMax_Should_AllowExport(int totalRows)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(new PaymentsReportExportReadResult(totalRows, Array.Empty<PaymentReportItemDto>()));

        var result = await CreateCsvHandler(exportReadEnabled: true)
            .Handle(CsvQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_AndCountExceedsMax_Should_ReturnSameTooManyRowsError()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(new PaymentsReportExportReadResult(
            PaymentsReportConstants.MaxExportRows + 1,
            Array.Empty<PaymentReportItemDto>()));

        var result = await CreateCsvHandler(exportReadEnabled: true)
            .Handle(CsvQuery(cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ReportExportTooManyRows");
        VerifyCommandExportPathNeverUsed();
    }

    [Fact]
    public async Task WhenQueryPath_Should_NotCallCommandCountOrList()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(EmptyExportResult());

        await CreateCsvHandler(exportReadEnabled: true).Handle(CsvQuery(cid), CancellationToken.None);

        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CsvExport_Should_BeUnaffectedByPaymentsReportReadEnabled(bool paymentsReportReadEnabled)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupCommandExportMocks();

        await CreateCsvHandler(
                exportReadEnabled: false,
                paymentsReportReadEnabled: paymentsReportReadEnabled)
            .Handle(CsvQuery(cid), CancellationToken.None);

        VerifyCommandExportPathUsed();
        VerifyExportReaderNeverCalled();
    }

    [Fact]
    public async Task JsonReport_Should_BeUnaffectedByPaymentsReportExportReadEnabled()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
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
            _jsonReportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsReportReadEnabled = false,
                PaymentsReportExportReadEnabled = true
            }));

        await handler.Handle(
            new GetPaymentsReportQuery(From, To, cid, null, null, null, null, 1, 20),
            CancellationToken.None);

        _jsonReportReader.Verify(
            r => r.GetReportAsync(It.IsAny<PaymentsReportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Pipeline_WhenQueryPath_Should_NotRunSearchResolution()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupTenantAndClinic(tid, cid);
        SetupExportReaderResult(EmptyExportResult());

        await PaymentsReportExportPipeline.LoadAsync(
            _tenant.Object,
            _clinic.Object,
            _scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            From,
            To,
            cid,
            null,
            null,
            null,
            search: null,
            paymentsSearchLookupEnabled: false,
            paymentsReportExportReadEnabled: true,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _exportReader.Object,
            CancellationToken.None);

        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ExportPaymentsReportQuery CsvQuery(Guid? clinicId, string? search = null)
        => new(From, To, clinicId, null, null, null, search);

    private static ExportPaymentsReportXlsxQuery XlsxQuery(Guid? clinicId, string? search = null)
        => new(From, To, clinicId, null, null, null, search);

    private static PaymentsReportExportReadResult EmptyExportResult()
        => new(0, Array.Empty<PaymentReportItemDto>());

    private void SetupTenantAndClinic(Guid tid, Guid clinicId)
    {
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
    }

    private void SetupExportReaderResult(PaymentsReportExportReadResult result)
        => _exportReader
            .Setup(r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

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

    private void VerifyCommandExportPathUsed()
    {
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void VerifyCommandExportPathNeverUsed()
    {
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void VerifyExportReaderNeverCalled()
        => _exportReader.Verify(
            r => r.GetExportAsync(It.IsAny<PaymentsReportExportReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private ExportPaymentsReportQueryHandler CreateCsvHandler(
        bool exportReadEnabled = false,
        bool paymentsReportReadEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _exportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsReportReadEnabled = paymentsReportReadEnabled,
                PaymentsReportExportReadEnabled = exportReadEnabled
            }));

    private ExportPaymentsReportXlsxQueryHandler CreateXlsxHandler(
        bool exportReadEnabled = false,
        Mock<IClinicReadScopeResolver>? scopeResolver = null)
        => new(
            _tenant.Object,
            _clinic.Object,
            (scopeResolver ?? _scopeResolver).Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            _exportReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsReportExportReadEnabled = exportReadEnabled
            }));
}
