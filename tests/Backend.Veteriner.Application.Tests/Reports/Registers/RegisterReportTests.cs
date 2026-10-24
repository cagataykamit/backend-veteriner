using System.Text;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Examinations;
using Backend.Veteriner.Application.Reports.Registers.Prescriptions;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Pets;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Registers;

public sealed class RegisterReportTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicA = Guid.NewGuid();
    private static readonly Guid ClinicB = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 13);

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IRegisterReader<ExaminationRegisterItemDto>> _reader = new();
    private Mock<IClinicReadScopeResolver> _scope = ClinicReadScopeResolverMock.Default();

    public RegisterReportTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<RegisterReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterReadResult<ExaminationRegisterItemDto>(0, []));
    }

    private RegisterReportRunner Runner() => new(_tenant.Object, _clinic.Object, _scope.Object, TrialLimitPolicyStub.Paid);

    private static RegisterQuery Query(DateOnly? from = null, DateOnly? to = null, Guid? clinicId = null, bool includeVoided = false)
        => new(from ?? Day, to ?? Day, clinicId, includeVoided);

    // ---- Doğrulama ve kapsam ----

    [Fact]
    public async Task ReadPage_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var r = await Runner().ReadPageAsync(_reader.Object, Query(), 1, 50, CancellationToken.None);

        r.Error.Code.Should().Be("Tenants.ContextMissing");
        _reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadPage_Should_Fail_When_A_Date_Is_Missing(bool fromMissing, bool toMissing)
    {
        var query = new RegisterQuery(fromMissing ? default : Day, toMissing ? default : Day, null, false);

        var r = await Runner().ReadPageAsync(_reader.Object, query, 1, 50, CancellationToken.None);

        r.Error.Code.Should().Be("Registers.DateRangeInvalid");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadPage_Should_Fail_When_From_After_To()
    {
        var r = await Runner().ReadPageAsync(_reader.Object, Query(from: Day.AddDays(1), to: Day), 1, 50, CancellationToken.None);

        r.Error.Code.Should().Be("Registers.DateRangeInvalid");
    }

    [Fact]
    public async Task ReadPage_Should_Accept_366_Days_And_Reject_367()
    {
        var ok = await Runner().ReadPageAsync(_reader.Object, Query(to: Day.AddDays(RegisterReportConstants.MaxRangeDays)), 1, 50, CancellationToken.None);
        var tooLong = await Runner().ReadPageAsync(_reader.Object, Query(to: Day.AddDays(RegisterReportConstants.MaxRangeDays + 1)), 1, 50, CancellationToken.None);

        ok.IsSuccess.Should().BeTrue();
        tooLong.Error.Code.Should().Be("Registers.RangeTooLong");
    }

    [Fact]
    public async Task ReadPage_Should_Fail_When_RequestClinic_Differs_From_ClinicContext()
    {
        _clinic.SetupGet(c => c.ClinicId).Returns(ClinicA);

        var r = await Runner().ReadPageAsync(_reader.Object, Query(clinicId: ClinicB), 1, 50, CancellationToken.None);

        r.Error.Code.Should().Be("Registers.ClinicContextMismatch");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadPage_Should_Propagate_Clinic_Scope_Failure()
    {
        _scope = ClinicReadScopeResolverMock.Default();
        _scope.SetupAccessDenied();

        var r = await Runner().ReadPageAsync(_reader.Object, Query(clinicId: ClinicB), 1, 50, CancellationToken.None);

        r.Error.Code.Should().Be("Clinics.AccessDenied");
        _reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadPage_Should_Pass_Istanbul_Day_Bounds_And_Clamped_Paging()
    {
        RegisterReadRequest? sent = null;
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<RegisterReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RegisterReadRequest, CancellationToken>((req, _) => sent = req)
            .ReturnsAsync(new RegisterReadResult<ExaminationRegisterItemDto>(0, []));

        var r = await Runner().ReadPageAsync(
            _reader.Object, Query(from: new DateOnly(2026, 1, 10), to: new DateOnly(2026, 1, 11), includeVoided: true), 3, 9999, CancellationToken.None);

        r.IsSuccess.Should().BeTrue();
        sent!.TenantId.Should().Be(TenantId);
        // Europe/Istanbul = UTC+3 (kalıcı): 10 Ocak 00:00 → 9 Ocak 21:00 UTC; 11 Ocak sonu → 11 Ocak 21:00 UTC (hariç).
        sent.FromUtc.Should().Be(new DateTime(2026, 1, 9, 21, 0, 0, DateTimeKind.Utc));
        sent.ToUtcExclusive.Should().Be(new DateTime(2026, 1, 11, 21, 0, 0, DateTimeKind.Utc));
        sent.IncludeVoided.Should().BeTrue();
        sent.Take.Should().Be(RegisterReportConstants.MaxPageSize);
        sent.Skip.Should().Be(2 * RegisterReportConstants.MaxPageSize);
    }

    [Fact]
    public async Task LoadForExport_Should_Fail_Above_The_Row_Ceiling()
    {
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<RegisterReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterReadResult<ExaminationRegisterItemDto>(ReportsSharedLimits.MaxExportRows + 1, []));

        var r = await Runner().LoadForExportAsync(_reader.Object, Query(), CancellationToken.None);

        r.Error.Code.Should().Be("Registers.ExportTooManyRows");
    }

    // ---- Görüntü kuralları ----

    [Theory]
    [InlineData(2023, 10, 13, "3 yıl")]
    [InlineData(2023, 8, 1, "3 yıl 2 ay")]
    [InlineData(2026, 4, 14, "5 ay")]
    [InlineData(2026, 4, 13, "6 ay")]
    [InlineData(2026, 10, 3, "10 gün")]
    public void Age_Should_Be_Computed_On_The_Given_Date(int y, int m, int d, string expected)
        => RegisterDisplay.Age(new DateOnly(y, m, d), Day).Should().Be(expected);

    [Fact]
    public void Age_Should_Be_Null_Without_Birth_Date_Or_When_Born_After()
    {
        RegisterDisplay.Age(null, Day).Should().BeNull();
        RegisterDisplay.Age(Day.AddDays(1), Day).Should().BeNull();
    }

    [Fact]
    public void Gender_Should_Be_Turkish()
    {
        RegisterDisplay.Gender(PetGender.Male).Should().Be("Erkek");
        RegisterDisplay.Gender(PetGender.Female).Should().Be("Dişi");
        RegisterDisplay.Gender(null).Should().BeNull();
    }

    // ---- Sütun tanımı ve dosyalar ----

    private static ExaminationRegisterItemDto Examination(int no = 1, bool voided = false) => new(
        no, Guid.NewGuid(), new DateTime(2026, 10, 12, 21, 30, 0, DateTimeKind.Utc), true,
        "Ayşe; \"Yılmaz\"", "905551112233", "Bornova / İzmir",
        "Pamuk", "Kedi", "Tekir", "Dişi", "3 yıl", null, "123456789", "P-7", null,
        "Kusma", "Gastrit",
        ["Serum", "Enjeksiyon"],
        [new RegisterPrescriptionDto("Reçete 1", [new RegisterPrescriptionLineDto("Amoksilin", 2.5m, null), new RegisterPrescriptionLineDto("Probiyotik", null, "günde 1")])],
        [new RegisterAdministrationDto("Aşı X", 1m, "doz", true, voided)],
        "Dr. Veli");

    [Fact]
    public void Examination_Csv_Should_Use_The_Column_Definition_Only()
    {
        var bytes = RegisterCsvWriter.WriteUtf8Bom(ExaminationRegisterColumns.All, [Examination()]);

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(';');
        header.Should().HaveCount(ExaminationRegisterColumns.All.Count);
        header[0].Should().Be("Sıra No");
        header[5].Should().Be("Hasta · Ad");
        header[10].Should().Be("Hasta · Kart No");
        header[^2].Should().Be("Tedavi Şekli, Reçete Bilgileri, Satışa Eklenen İlaçlar");
        header[^1].Should().Be("Veteriner Hekimin Adı Soyadı");

        lines[1].Should().StartWith("1;13.10.2026;\"Ayşe; \"\"Yılmaz\"\"\";905551112233;Bornova / İzmir;Pamuk;Kedi;Tekir;Dişi;3 yıl;;123456789;P-7;;Kusma;Gastrit;");
        lines[1].Should().EndWith("\"Serum; Enjeksiyon; Reçete: Reçete 1: Amoksilin ×2,5, Probiyotik; Satış: Aşı X 1 doz (sonradan eklendi)\";Dr. Veli");
    }

    [Fact]
    public void Treatment_Prescription_Sale_Cell_Should_Skip_Empty_Sections()
    {
        var empty = Examination() with { Treatments = [], Prescriptions = [], Administrations = [] };
        var onlySale = empty with { Administrations = [new RegisterAdministrationDto("Aşı X", 1m, "doz", false, false)] };
        var cell = ExaminationRegisterColumns.All.Single(c => c.Header.StartsWith("Tedavi Şekli"));

        cell.Value(empty).Should().Be(string.Empty);
        cell.Value(onlySale).Should().Be("Satış: Aşı X 1 doz");
    }

    [Fact]
    public void Voided_Administration_Should_Be_Marked_In_The_File_Cell()
    {
        var bytes = RegisterCsvWriter.WriteUtf8Bom(ExaminationRegisterColumns.All, [Examination(voided: true)]);

        Encoding.UTF8.GetString(bytes).Should().Contain("Aşı X 1 doz (sonradan eklendi) (iptal)");
    }

    [Fact]
    public void Examination_Xlsx_Should_Have_Grouped_Header_And_Typed_Cells()
    {
        var bytes = RegisterXlsxWriter.WriteWorkbook("Muayene Defteri", ExaminationRegisterColumns.All, [Examination(5)]);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        ws.Cell(1, 6).GetString().Should().Be("Hasta");
        ws.Cell(1, 6).IsMerged().Should().BeTrue();
        ws.Cell(2, 6).GetString().Should().Be("Ad");
        ws.Cell(1, 1).GetString().Should().Be("Sıra No");
        ws.Cell(3, 1).GetDouble().Should().Be(5);
        ws.Cell(3, 2).GetDateTime().Should().Be(new DateTime(2026, 10, 13));
        ws.Cell(3, 2).Style.DateFormat.Format.Should().Be("dd.MM.yyyy");
        ws.Cell(3, 6).GetString().Should().Be("Pamuk");
        ws.Cell(3, 11).IsEmpty().Should().BeTrue();
    }

    [Fact]
    public void Prescription_Row_Should_Combine_Drugs_In_One_Cell()
    {
        var entry = new PrescriptionRegisterEntryDto(
            3, Guid.NewGuid(), new DateTime(2026, 10, 12, 21, 30, 0, DateTimeKind.Utc),
            null, null, "Dr. Veli", "Klinik Adresi", "Ayşe Yılmaz", null, "Köpek", null, 1,
            [new PrescriptionRegisterItemDto("Amoksilin", null, null, 2m), new PrescriptionRegisterItemDto("Probiyotik", null, null, null)]);

        var csv = Encoding.UTF8.GetString(RegisterCsvWriter.WriteUtf8Bom(PrescriptionRegisterColumns.All, [entry]))
            .TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        csv[0].Should().StartWith("Sıra No;Reçetenin · Tarihi;Reçetenin · Sınıfı;Reçetenin · Cilt/Sıra No;Veteriner Hekimin · Adı Soyadı;");
        csv.Should().HaveCount(2);
        csv[1].Should().Be("3;13.10.2026;;;Dr. Veli;Klinik Adresi;Ayşe Yılmaz;;Köpek;;1;\"Amoksilin ×2; Probiyotik\"");
    }

    [Fact]
    public async Task Empty_Result_Should_Produce_Header_Only_Files()
    {
        var handler = new ExportExaminationRegisterQueryHandler(Runner(), _reader.Object);
        var xlsxHandler = new ExportExaminationRegisterXlsxQueryHandler(Runner(), _reader.Object);

        var csv = await handler.Handle(new ExportExaminationRegisterQuery(Day, Day, null, false), CancellationToken.None);
        var xlsx = await xlsxHandler.Handle(new ExportExaminationRegisterXlsxQuery(Day, Day, null, false), CancellationToken.None);

        csv.Value!.FileDownloadName.Should().MatchRegex(@"^muayene-defteri-\d{8}\.csv$");
        Encoding.UTF8.GetString(csv.Value.Content).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1);
        xlsx.Value!.FileDownloadName.Should().MatchRegex(@"^muayene-defteri-\d{8}\.xlsx$");
        using var wb = new XLWorkbook(new MemoryStream(xlsx.Value.Content));
        wb.Worksheet(1).LastRowUsed()!.RowNumber().Should().Be(2);
    }
}
