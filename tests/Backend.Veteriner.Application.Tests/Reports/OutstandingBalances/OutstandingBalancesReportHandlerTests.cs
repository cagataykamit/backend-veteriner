using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.ExportOutstandingBalancesReport;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.GetOutstandingBalancesReport;
using Backend.Veteriner.Application.Tests.TestHelpers;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.OutstandingBalances;

public sealed class OutstandingBalancesReportHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicA = Guid.NewGuid();
    private static readonly Guid ClinicB = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IOutstandingBalancesReportReader> _reader = new();
    private Mock<IClinicReadScopeResolver> _scope = ClinicReadScopeResolverMock.Default();

    public OutstandingBalancesReportHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<OutstandingBalancesReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutstandingBalancesReadResult(0, 0m, []));
    }

    private GetOutstandingBalancesReportQueryHandler Handler()
        => new(_tenant.Object, _clinic.Object, _scope.Object, _reader.Object);

    private ExportOutstandingBalancesReportQueryHandler CsvHandler()
        => new(_tenant.Object, _clinic.Object, _scope.Object, _reader.Object, TrialLimitPolicyStub.Paid);

    private ExportOutstandingBalancesReportXlsxQueryHandler XlsxHandler()
        => new(_tenant.Object, _clinic.Object, _scope.Object, _reader.Object, TrialLimitPolicyStub.Paid);

    private static GetOutstandingBalancesReportQuery Query(
        Guid? clinicId = null, decimal? minAmount = null, string? search = null, int page = 1, int pageSize = 50)
        => new(clinicId, minAmount, search, page, pageSize);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var r = await Handler().Handle(Query(), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Tenants.ContextMissing");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_Fail_When_MinAmount_Negative()
    {
        var r = await Handler().Handle(Query(minAmount: -1m), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("OutstandingBalances.MinAmountInvalid");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RequestClinic_Differs_From_ClinicContext()
    {
        _clinic.SetupGet(c => c.ClinicId).Returns(ClinicA);

        var r = await Handler().Handle(Query(clinicId: ClinicB), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("OutstandingBalances.ClinicContextMismatch");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_Propagate_Clinic_Scope_Failure()
    {
        _scope = ClinicReadScopeResolverMock.Default();
        _scope.SetupAccessDenied();

        var r = await Handler().Handle(Query(clinicId: ClinicB), CancellationToken.None);

        r.IsSuccess.Should().BeFalse();
        r.Error.Code.Should().Be("Clinics.AccessDenied");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Should_Use_Active_Clinic_Context_When_Request_ClinicId_Missing()
    {
        _clinic.SetupGet(c => c.ClinicId).Returns(ClinicA);

        await Handler().Handle(Query(), CancellationToken.None);

        _reader.Verify(x => x.ReadAsync(
            It.Is<OutstandingBalancesReadRequest>(q => q.TenantId == TenantId && q.ClinicId == ClinicA),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Pass_Accessible_Clinics_For_ClinicAdmin_Without_Context()
    {
        _scope = ClinicReadScopeResolverMock.ForClinicAdmin([ClinicA, ClinicB]);

        await Handler().Handle(Query(), CancellationToken.None);

        _reader.Verify(x => x.ReadAsync(
            It.Is<OutstandingBalancesReadRequest>(q => q.ClinicId == null
                && q.AccessibleClinicIds != null && q.AccessibleClinicIds.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(1, 50, 0, 50)]
    [InlineData(3, 20, 40, 20)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(-5, 100000, 0, 200)]
    public async Task Handle_Should_Normalize_Paging(int page, int pageSize, int expectedSkip, int expectedTake)
    {
        await Handler().Handle(Query(page: page, pageSize: pageSize), CancellationToken.None);

        _reader.Verify(x => x.ReadAsync(
            It.Is<OutstandingBalancesReadRequest>(q => q.Skip == expectedSkip && q.Take == expectedTake),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Parse_Search_And_Return_Try_Totals_From_Reader()
    {
        var item = Item("Ayşe Yılmaz", 600m);
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<OutstandingBalancesReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutstandingBalancesReadResult(7, 1234.5m, [item]));

        var r = await Handler().Handle(Query(minAmount: 10m, search: "  ayse 0532 "), CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        r.Value!.Currency.Should().Be("TRY");
        r.Value.TotalCount.Should().Be(7);
        r.Value.TotalOutstandingAmount.Should().Be(1234.5m);
        r.Value.Items.Should().ContainSingle().Which.Should().Be(item);
        _reader.Verify(x => x.ReadAsync(
            It.Is<OutstandingBalancesReadRequest>(q => q.MinAmount == 10m && q.Search != null && q.Search.Tokens.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Pass_No_Search_When_Blank()
    {
        await Handler().Handle(Query(search: "   "), CancellationToken.None);

        _reader.Verify(x => x.ReadAsync(
            It.Is<OutstandingBalancesReadRequest>(q => q.Search == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Dışa aktarma ----

    [Fact]
    public async Task Export_Should_Read_All_Rows_With_Same_Filter_For_Csv_And_Xlsx()
    {
        _clinic.SetupGet(c => c.ClinicId).Returns(ClinicA);

        await CsvHandler().Handle(new ExportOutstandingBalancesReportQuery(null, 5m, "ali"), CancellationToken.None);
        await XlsxHandler().Handle(new ExportOutstandingBalancesReportXlsxQuery(null, 5m, "ali"), CancellationToken.None);

        _reader.Verify(x => x.ReadAsync(
            It.Is<OutstandingBalancesReadRequest>(q => q.Skip == 0 && q.Take == OutstandingBalancesReportConstants.MaxExportRows
                && q.ClinicId == ClinicA && q.MinAmount == 5m && q.Search != null),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Export_Should_Fail_When_Rows_Exceed_Limit()
    {
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<OutstandingBalancesReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutstandingBalancesReadResult(OutstandingBalancesReportConstants.MaxExportRows + 1, 1m, []));

        var csv = await CsvHandler().Handle(new ExportOutstandingBalancesReportQuery(null, null, null), CancellationToken.None);
        var xlsx = await XlsxHandler().Handle(new ExportOutstandingBalancesReportXlsxQuery(null, null, null), CancellationToken.None);

        csv.Error.Code.Should().Be("OutstandingBalances.ReportExportTooManyRows");
        xlsx.Error.Code.Should().Be("OutstandingBalances.ReportExportTooManyRows");
    }

    [Fact]
    public async Task Export_Should_Fail_When_MinAmount_Negative_Or_Tenant_Missing()
    {
        var bad = await CsvHandler().Handle(new ExportOutstandingBalancesReportQuery(null, -0.01m, null), CancellationToken.None);
        bad.Error.Code.Should().Be("OutstandingBalances.MinAmountInvalid");

        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var noTenant = await XlsxHandler().Handle(new ExportOutstandingBalancesReportXlsxQuery(null, null, null), CancellationToken.None);
        noTenant.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Csv_Should_Have_Bom_Semicolons_Turkish_Headers_And_Istanbul_Date()
    {
        // 2026-03-31 21:30 UTC = 2026-04-01 00:30 İstanbul (UTC+3)
        var rows = new[]
        {
            Item("Ayşe; \"Yılmaz\"", 1234.5m, phone: "905321234567", count: 3, oldest: new DateTime(2026, 3, 31, 21, 30, 0, DateTimeKind.Utc)),
            Item("Veli", 90m, phone: null, count: 1, oldest: new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc)),
        };
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<OutstandingBalancesReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutstandingBalancesReadResult(2, 1324.5m, rows));

        var result = await CsvHandler().Handle(new ExportOutstandingBalancesReportQuery(null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileDownloadName.Should().MatchRegex(@"^borc-raporu-\d{8}\.csv$");
        var bytes = result.Value.ContentUtf8Bom;
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var lines = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Müşteri;Telefon;Kalan Tutar;Para Birimi;Açık Ücret Sayısı;En Eski Açık Ücret");
        lines[1].Should().Be("\"Ayşe; \"\"Yılmaz\"\"\";905321234567;1234,50;TRY;3;01.04.2026");
        lines[2].Should().Be("Veli;;90,00;TRY;1;05.01.2026");
        lines.Should().HaveCount(3);
    }

    [Fact]
    public async Task Xlsx_Should_Have_Same_Headers_And_Typed_Cells()
    {
        var rows = new[]
        {
            Item("Ayşe Yılmaz", 1234.5m, phone: "905321234567", count: 3, oldest: new DateTime(2026, 3, 31, 21, 30, 0, DateTimeKind.Utc)),
        };
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<OutstandingBalancesReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutstandingBalancesReadResult(1, 1234.5m, rows));

        var result = await XlsxHandler().Handle(new ExportOutstandingBalancesReportXlsxQuery(null, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileDownloadName.Should().MatchRegex(@"^borc-raporu-\d{8}\.xlsx$");
        using var wb = new XLWorkbook(new MemoryStream(result.Value.Content));
        var ws = wb.Worksheet(1);
        Enumerable.Range(1, 6).Select(c => ws.Cell(1, c).GetString()).Should().Equal(
            "Müşteri", "Telefon", "Kalan Tutar", "Para Birimi", "Açık Ücret Sayısı", "En Eski Açık Ücret");
        ws.Cell(2, 1).GetString().Should().Be("Ayşe Yılmaz");
        ws.Cell(2, 2).GetString().Should().Be("905321234567");
        ws.Cell(2, 3).GetDouble().Should().Be(1234.5);
        ws.Cell(2, 4).GetString().Should().Be("TRY");
        ws.Cell(2, 5).GetDouble().Should().Be(3);
        ws.Cell(2, 6).GetDateTime().Should().Be(new DateTime(2026, 4, 1));
    }

    private static OutstandingBalanceItemDto Item(
        string name, decimal amount, string? phone = "905551110000", int count = 1, DateTime? oldest = null)
        => new(Guid.NewGuid(), name, phone, amount, count, oldest ?? new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc));
}
