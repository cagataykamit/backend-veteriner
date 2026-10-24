using System.Text;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Payments;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.DailyTransactions;

public sealed class DailyTransactionsReportTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClinicReadScopeResolver> _scope = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IDailyTransactionsReader> _reader = new();

    public DailyTransactionsReportTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyTransactionsReadResult(0, Totals(), []));
    }

    private static DailyTransactionTotalsDto Totals()
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static CashEventRow Cash(
        bool voided, string at, decimal amount, PaymentMethod method, bool sale = false,
        Guid? collectedBy = null, Guid? performedBy = null, string? reason = null)
        => new(voided, DateTime.Parse(at, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
            Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Müşteri", amount, method, sale,
            collectedBy, collectedBy is null ? null : (collectedBy == UserA ? "Ayşe" : "Bora"),
            performedBy, performedBy is null ? null : (performedBy == UserA ? "Ayşe" : "Bora"), reason);

    [Fact]
    public async Task Get_Should_Fail_For_Reversed_And_Too_Long_Ranges_Without_Reading()
    {
        var handler = new GetDailyTransactionsReportQueryHandler(_tenant.Object, _clinic.Object, _scope.Object, _reader.Object);
        var day = new DateOnly(2026, 10, 14);

        var reversed = await handler.Handle(new GetDailyTransactionsReportQuery(day, day.AddDays(-1), null, null, null, null, 1, 50), CancellationToken.None);
        var tooLong = await handler.Handle(new GetDailyTransactionsReportQuery(day, day.AddDays(367), null, null, null, null, 1, 50), CancellationToken.None);
        var max = await handler.Handle(new GetDailyTransactionsReportQuery(day, day.AddDays(366), null, null, null, null, 1, 50), CancellationToken.None);

        reversed.Error.Code.Should().Be("Registers.DateRangeInvalid");
        tooLong.Error.Code.Should().Be("Registers.RangeTooLong");
        max.IsSuccess.Should().BeTrue();
        _reader.Verify(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_Should_Pass_Istanbul_Day_Window_And_Clamp_Paging()
    {
        FinanceEventFilter? seen = null;
        int seenSkip = 0, seenTake = 0;
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((FinanceEventFilter f, int s, int t, CancellationToken _) => (seen, seenSkip, seenTake) = (f, s, t))
            .ReturnsAsync(new DailyTransactionsReadResult(0, Totals(), []));
        var handler = new GetDailyTransactionsReportQueryHandler(_tenant.Object, _clinic.Object, _scope.Object, _reader.Object);
        var day = new DateOnly(2026, 10, 14);

        await handler.Handle(
            new GetDailyTransactionsReportQuery(day, day, ClinicId, DailyTransactionKind.Payment, PaymentMethod.Card, UserA, 3, 9999),
            CancellationToken.None);

        // Europe/Istanbul UTC+3: yerel 14 Ekim 00:00 = 13 Ekim 21:00 UTC; bitiş ertesi yerel gün başı (hariç).
        seen!.FromUtc.Should().Be(new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc));
        seen.ToUtcExclusive.Should().Be(new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));
        seen.TenantId.Should().Be(TenantId);
        seen.ClinicId.Should().Be(ClinicId);
        seen.Kind.Should().Be(DailyTransactionKind.Payment);
        seen.PaymentMethod.Should().Be(PaymentMethod.Card);
        seen.PerformedByUserId.Should().Be(UserA);
        seenTake.Should().Be(RegisterReportConstants.MaxPageSize);
        seenSkip.Should().Be(2 * RegisterReportConstants.MaxPageSize);
    }

    [Fact]
    public async Task Export_Should_Fail_Above_Row_Ceiling_And_Write_Csv_And_Xlsx_From_Same_Rows()
    {
        var item = new DailyTransactionItemDto(
            new DateTime(2026, 10, 14, 7, 5, 0, DateTimeKind.Utc), DailyTransactionKind.PaymentVoid, Guid.NewGuid(), ClinicId, Guid.NewGuid(),
            "Ahmet; Yılmaz", Guid.NewGuid(), "Pamuk", "not", 1234.5m, PaymentMethod.Transfer, true, true, "Yanlış giriş", UserA, "Ayşe");
        var correction = item with { Kind = DailyTransactionKind.ChargeCorrection, Amount = null, PaymentMethod = null, IsDirectSale = false, IsVoided = false };
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), 0, RegisterReportConstants.MaxExportRows, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyTransactionsReadResult(2, Totals(), [item, correction]));
        var handler = new ExportDailyTransactionsReportQueryHandler(_tenant.Object, _clinic.Object, _scope.Object, _reader.Object, TrialLimitPolicyStub.Paid);
        var day = new DateOnly(2026, 10, 14);

        var csv = await handler.Handle(new ExportDailyTransactionsReportQuery(day, day, null, null, null, null, RegisterExportFormat.Csv), CancellationToken.None);
        var xlsx = await handler.Handle(new ExportDailyTransactionsReportQuery(day, day, null, null, null, null, RegisterExportFormat.Xlsx), CancellationToken.None);

        csv.IsSuccess.Should().BeTrue();
        csv.Value!.FileDownloadName.Should().MatchRegex(@"^gunluk-islem-dokumu-\d{8}\.csv$");
        var text = Encoding.UTF8.GetString(csv.Value.Content);
        text.Should().StartWith("﻿Tarih Saat;Tür;Müşteri;Hayvan;Açıklama;Tutar;Ödeme Türü;Kaynak;Durum;Gerekçe;İşlemi Yapan");
        var lines = text.TrimEnd().Split("\r\n");
        // 07:05 UTC = 10:05 İstanbul
        lines[1].Should().Be("14.10.2026 10:05;Tahsilat iptali;\"Ahmet; Yılmaz\";Pamuk;not;1234,50;Havale-EFT;Doğrudan satış;İptal edildi;Yanlış giriş;Ayşe");
        lines[2].Should().Be("14.10.2026 10:05;Ücret düzeltmesi;\"Ahmet; Yılmaz\";Pamuk;not;;;;Geçerli;Yanlış giriş;Ayşe");

        xlsx.Value!.FileDownloadName.Should().EndWith(".xlsx");
        using var wb = new XLWorkbook(new MemoryStream(xlsx.Value.Content));
        var ws = wb.Worksheet(1);
        ws.Cell(2, 1).GetDateTime().Should().Be(new DateTime(2026, 10, 14, 10, 5, 0));
        ws.Cell(2, 2).GetString().Should().Be("Tahsilat iptali");
        ws.Cell(2, 6).GetDouble().Should().Be(1234.5);
        ws.Cell(3, 6).IsEmpty().Should().BeTrue();
        ws.LastRowUsed()!.RowNumber().Should().Be(3);

        _reader
            .Setup(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), 0, RegisterReportConstants.MaxExportRows, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyTransactionsReadResult(RegisterReportConstants.MaxExportRows + 1, Totals(), []));
        var tooMany = await handler.Handle(new ExportDailyTransactionsReportQuery(day, day, null, null, null, null, RegisterExportFormat.Csv), CancellationToken.None);
        tooMany.Error.Code.Should().Be("Registers.ExportTooManyRows");
    }

    [Fact]
    public void CashSummary_Should_Aggregate_Method_Day_And_User_From_Same_Rows()
    {
        var rows = new List<CashEventRow>
        {
            // 14 Ekim (İstanbul) günü
            Cash(false, "2026-10-14T07:00:00Z", 600m, PaymentMethod.Cash, collectedBy: UserA),
            Cash(false, "2026-10-14T08:00:00Z", 200m, PaymentMethod.Card, collectedBy: UserB),
            Cash(false, "2026-10-14T09:00:00Z", 20m, PaymentMethod.Cash, sale: true, collectedBy: UserA),
            // B'nin kart tahsilatı A tarafından iptal edildi
            Cash(true, "2026-10-14T10:00:00Z", 200m, PaymentMethod.Card, collectedBy: UserB, performedBy: UserA, reason: "Yanlış giriş"),
            // 14 Ekim 23:30 UTC = 15 Ekim 02:30 İstanbul (ertesi gün)
            Cash(false, "2026-10-14T23:30:00Z", 50m, PaymentMethod.Transfer, collectedBy: null),
        };

        var report = CashSummaryBuilder.Build(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15), rows);

        report.Totals.CollectedAmount.Should().Be(870m);
        report.Totals.CollectedCount.Should().Be(4);
        report.Totals.VoidedAmount.Should().Be(200m);
        report.Totals.NetAmount.Should().Be(670m);
        report.Totals.SalesCollectedAmount.Should().Be(20m);

        report.ByMethod.Select(m => m.PaymentMethod).Should().Equal(PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Transfer);
        report.ByMethod[0].NetAmount.Should().Be(620m);
        report.ByMethod[1].CollectedAmount.Should().Be(200m);
        report.ByMethod[1].VoidedAmount.Should().Be(200m);
        report.ByMethod[1].NetAmount.Should().Be(0m);
        report.ByMethod.Sum(m => m.NetAmount).Should().Be(report.Totals.NetAmount);

        report.ByDay.Select(d => d.Date).Should().Equal(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15));
        report.ByDay[0].NetAmount.Should().Be(620m);
        report.ByDay[0].CashNetAmount.Should().Be(620m);
        report.ByDay[0].CardNetAmount.Should().Be(0m);
        report.ByDay[1].TransferNetAmount.Should().Be(50m);
        report.ByDay.Sum(d => d.NetAmount).Should().Be(report.Totals.NetAmount);

        // Tahsilat alana, iptal iptal edene yazılır; tahsisi olmayan eski ödeme "Bilinmiyor".
        report.ByUser.Sum(u => u.NetAmount).Should().Be(report.Totals.NetAmount);
        var a = report.ByUser.Single(u => u.UserId == UserA);
        a.CollectedAmount.Should().Be(620m);
        a.VoidedAmount.Should().Be(200m);
        a.NetAmount.Should().Be(420m);
        report.ByUser.Single(u => u.UserId == UserB).NetAmount.Should().Be(200m);
        report.ByUser.Single(u => u.UserId == null).UserName.Should().Be("Bilinmiyor");

        var voided = report.VoidedPayments.Should().ContainSingle().Subject;
        voided.Amount.Should().Be(200m);
        voided.Reason.Should().Be("Yanlış giriş");
        voided.CollectedByUserId.Should().Be(UserB);
        voided.VoidedByUserId.Should().Be(UserA);
    }

    [Fact]
    public void CashSummary_Should_Always_List_Three_Methods_And_Export_Flat_Sections()
    {
        var report = CashSummaryBuilder.Build(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14), []);
        report.ByMethod.Should().HaveCount(3).And.OnlyContain(m => m.NetAmount == 0m);
        report.ByDay.Should().BeEmpty();
        report.Totals.NetAmount.Should().Be(0m);

        var full = CashSummaryBuilder.Build(
            new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14),
            [Cash(false, "2026-10-14T07:00:00Z", 100m, PaymentMethod.Card, collectedBy: UserA)]);
        var export = RegisterReportRunner.BuildFile(CashSummaryBuilder.ExportSpec, CashSummaryBuilder.ToExportRows(full), RegisterExportFormat.Csv);

        export.FileDownloadName.Should().MatchRegex(@"^kasa-ozeti-\d{8}\.csv$");
        var lines = Encoding.UTF8.GetString(export.Content).TrimEnd().Split("\r\n");
        lines[0].Should().Be("﻿Bölüm;Ad;Tahsilat Adedi;Tahsilat Tutarı;İptal Adedi;İptal Tutarı;Net");
        lines[1].Should().Be("Toplam;Toplam;1;100,00;0;0,00;100,00");
        lines.Should().Contain("Ödeme Türü;Kart;1;100,00;0;0,00;100,00");
        lines.Should().Contain("Ödeme Türü;Nakit;0;0,00;0;0,00;0,00");
        lines.Should().Contain("Gün;14.10.2026;1;100,00;0;0,00;100,00");
        lines.Should().Contain("Kullanıcı;Ayşe;1;100,00;0;0,00;100,00");
    }

    [Fact]
    public async Task EndOfDay_Should_Require_Single_Clinic_And_Pass_Window_To_Shared_Open_Items_Reader()
    {
        var openItems = new Mock<IVisitOpenItemsReader>();
        OpenItemsSummaryRequest? seen = null;
        openItems
            .Setup(r => r.GetSummaryAsync(It.IsAny<OpenItemsSummaryRequest>(), It.IsAny<CancellationToken>()))
            .Callback((OpenItemsSummaryRequest r, CancellationToken _) => seen = r)
            .ReturnsAsync(new OpenItemsSummaryDto(2, 1, 150m, 3, 4));
        var permissions = new Mock<ICurrentUserPermissionChecker>();
        permissions.Setup(p => p.HasPermission(PermissionCatalog.Charges.Read)).Returns(true);
        var handler = new GetEndOfDayOpenItemsQueryHandler(_tenant.Object, _clinic.Object, _scope.Object, permissions.Object, openItems.Object);
        var day = new DateOnly(2026, 10, 14);

        var noClinic = await handler.Handle(new GetEndOfDayOpenItemsQuery(day, day, null), CancellationToken.None);
        noClinic.Error.Code.Should().Be("EndOfDay.ClinicScopeRequired");
        openItems.VerifyNoOtherCalls();

        var ok = await handler.Handle(new GetEndOfDayOpenItemsQuery(day, day.AddDays(1), ClinicId), CancellationToken.None);

        ok.IsSuccess.Should().BeTrue();
        ok.Value!.ClinicId.Should().Be(ClinicId);
        ok.Value.OpenItems.IncompleteVisitCount.Should().Be(4);
        seen!.Scope.TenantId.Should().Be(TenantId);
        seen.Scope.ClinicId.Should().Be(ClinicId);
        seen.Scope.Sees(OpenItemSources.Charges).Should().BeTrue();
        seen.Scope.Sees(OpenItemSources.LabResults).Should().BeFalse();
        seen.UnchargedFromUtc.Should().Be(new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc));
        seen.UnchargedToUtc.Should().Be(new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc));
        seen.IncompleteBeforeUtc.Should().Be(seen.UnchargedToUtc);
    }
}
