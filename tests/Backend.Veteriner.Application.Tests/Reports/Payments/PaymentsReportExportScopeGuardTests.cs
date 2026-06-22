using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Payments;

/// <summary>CQRS-15I: Export Command DB route, scope guard ve JSON report flag bağımsızlığı.</summary>
public sealed class PaymentsReportExportScopeGuardTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IClientReadModelLookupReader> _clientLookupReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CsvExport_Should_UseCommandDb_EvenWhenPaymentsReportReadEnabled(bool paymentsReportReadEnabled)
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportMocks();

        var handler = CreateCsvHandler(
            paymentsReportReadEnabled: paymentsReportReadEnabled,
            paymentsSearchLookupEnabled: false);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var result = await handler.Handle(new ExportPaymentsReportQuery(from, to, null, null, null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task XlsxExport_Should_UseCommandDb_EvenWhenPaymentsReportReadEnabled(bool paymentsReportReadEnabled)
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportMocks();

        var handler = CreateXlsxHandler(
            paymentsReportReadEnabled: paymentsReportReadEnabled,
            paymentsSearchLookupEnabled: false);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var result = await handler.Handle(
            new ExportPaymentsReportXlsxQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsFilteredOrderedForReportSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CsvExport_WithSearch_Should_UseCommandSearchResolution_WhenLookupFlagFalse()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupTenantAndClinic(tid, clinicId);
        SetupExportMocks();
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        await CreateCsvHandler(paymentsSearchLookupEnabled: false).Handle(
            new ExportPaymentsReportQuery(from, to, clinicId, null, null, null, "pamuk"),
            CancellationToken.None);

        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _clientLookupReader.Verify(
            r => r.ResolveClientIdsByTextSearchAsync(It.IsAny<ClientTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CsvExport_Should_Fail_When_ClinicContextMismatch_BeforeCommandDbAccess()
    {
        var tid = Guid.NewGuid();
        var jwtClinic = Guid.NewGuid();
        var bodyClinic = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(jwtClinic);

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var result = await CreateCsvHandler().Handle(
            new ExportPaymentsReportQuery(from, to, bodyClinic, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Payments.ClinicContextMismatch");
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CsvExport_Should_AllowTenantWideScope_WhenClinicContextMissing()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupExportMocks();

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var result = await CreateCsvHandler().Handle(
            new ExportPaymentsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.CountAsync(It.IsAny<PaymentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CsvExport_Should_ResolveMultiClinicScope_ForClinicAdminWithoutClinicId()
    {
        var tid = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupExportMocks();

        var scopeResolver = ClinicReadScopeResolverMock.ForClinicAdmin(new[] { c1, c2 });
        var handler = new ExportPaymentsReportQueryHandler(
            _tenant.Object,
            _clinic.Object,
            scopeResolver.Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions()));

        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var result = await handler.Handle(
            new ExportPaymentsReportQuery(from, to, null, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        scopeResolver.Verify(
            r => r.ResolveAsync(tid, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupTenantAndClinic(Guid tid, Guid clinicId)
    {
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(clinicId);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(tid, "K", "İstanbul"));
    }

    private void SetupExportMocks()
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

    private ExportPaymentsReportQueryHandler CreateCsvHandler(
        bool paymentsReportReadEnabled = false,
        bool paymentsSearchLookupEnabled = false)
        => new(
            _tenant.Object,
            _clinic.Object,
            ClinicReadScopeResolverMock.Default().Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsReportReadEnabled = paymentsReportReadEnabled,
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));

    private ExportPaymentsReportXlsxQueryHandler CreateXlsxHandler(
        bool paymentsReportReadEnabled = false,
        bool paymentsSearchLookupEnabled = false)
        => new(
            _tenant.Object,
            _clinic.Object,
            ClinicReadScopeResolverMock.Default().Object,
            _payments.Object,
            _clients.Object,
            _pets.Object,
            _clinics.Object,
            _clientLookupReader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                PaymentsReportReadEnabled = paymentsReportReadEnabled,
                PaymentsSearchLookupEnabled = paymentsSearchLookupEnabled
            }));
}
