using System.Text;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;
using Backend.Veteriner.Application.Reports.Registers.Medications;
using Backend.Veteriner.Application.Reports.Registers.VaccineInfo;
using Backend.Veteriner.Application.Reports.Registers.Vaccines;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using ClosedXML.Excel;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Reports.Registers;

/// <summary>
/// Parti bazlı defterler (aşı, ilaç, narkotik/psikotropik, aşı bilgi): sütun tanımı ↔ CSV/XLSX, hücre biçimleri ve
/// narkotik defterinin taraf kuralları (REGISTERS_API_CONTRACT.md bölüm II).
/// </summary>
public sealed class BatchRegisterTests
{
    // Europe/Istanbul = UTC+3 (kalıcı): 12 Ekim 21:30 UTC = 13 Ekim 00:30 İstanbul.
    private static readonly DateTime Moment = new(2026, 10, 12, 21, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly MomentDay = new(2026, 10, 13);

    private static string[] CsvLines(byte[] bytes)
        => Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    // ---- Aşı kayıt defteri ----

    private static VaccineRegisterItemDto Vaccine(bool batched = true, bool reversed = false) => new(
        2, Guid.NewGuid(), Guid.NewGuid(), "Nobivac DHPPi",
        batched ? "LOT-1" : null, batched ? new DateOnly(2027, 5, 31) : null, 20m,
        batched ? "Vet Depo A.Ş." : null, batched ? "Soğuk zincir aracı" : null, batched ? "İndikatör X" : null,
        batched ? new DateOnly(2026, 10, 10) : null, new DateOnly(2026, 10, 12),
        batched ? new DateOnly(2026, 10, 11) : null, batched ? "F-100" : null, batched, reversed);

    [Fact]
    public void Vaccine_Csv_Should_Follow_The_Documented_Columns()
    {
        var lines = CsvLines(RegisterCsvWriter.WriteUtf8Bom(VaccineRegisterColumns.All, [Vaccine()]));

        lines[0].Should().Be(
            "Sıra No;Ticari Adı;Seri No;Son Kullanma Tarihi;Toplam Miktar (Doz);Satın Alınan Firma;Nakil Şekli (Soğuk Zincir);"
            + "Nakilde Kullanılan İzleme Cihazı (Soğuk Zincir İndikatörü);Depo Çıkış Tarihi;Teslim Tarihi;Fatura Tarihi;Fatura No");
        lines[1].Should().Be(
            "2;Nobivac DHPPi;LOT-1;31.05.2027;20;Vet Depo A.Ş.;Soğuk zincir aracı;İndikatör X;10.10.2026;12.10.2026;11.10.2026;F-100");
    }

    [Fact]
    public void Vaccine_Unbatched_Entry_Should_Leave_Batch_Columns_Empty_And_Reversed_Should_Be_Marked()
    {
        var lines = CsvLines(RegisterCsvWriter.WriteUtf8Bom(VaccineRegisterColumns.All, [Vaccine(batched: false, reversed: true)]));

        lines[1].Should().Be("2;Nobivac DHPPi (iptal);;;20;;;;;12.10.2026;;");
    }

    [Fact]
    public void Vaccine_Xlsx_Should_Have_Calendar_Dates_Without_Timezone_Shift()
    {
        var bytes = RegisterXlsxWriter.WriteWorkbook(
            VaccineRegisterColumns.Export.SheetName, VaccineRegisterColumns.All, [Vaccine()]);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        ws.Name.Should().Be("Aşı Kayıt Defteri");
        ws.Cell(1, 4).GetString().Should().Be("Son Kullanma Tarihi");
        ws.Cell(2, 4).GetDateTime().Should().Be(new DateTime(2027, 5, 31));
        ws.Cell(2, 4).Style.DateFormat.Format.Should().Be("dd.MM.yyyy");
        ws.Cell(2, 5).GetDouble().Should().Be(20);
    }

    // ---- İlaç kayıt defteri ----

    [Fact]
    public void Medication_Csv_Should_Follow_The_Documented_Columns()
    {
        var item = new MedicationRegisterItemDto(
            1, Guid.NewGuid(), Guid.NewGuid(), "Amoksilin", "Enjeksiyonluk çözelti", "Flakon", 12.5m, "kutu",
            "Vet Depo", new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 12), "F-7", null, false, false);

        var lines = CsvLines(RegisterCsvWriter.WriteUtf8Bom(MedicationRegisterColumns.All, [item]));

        lines[0].Should().Be("Sıra No;Ürün;Farmakolojik Şekli;Takdim Şekli;Adet;Birim;Firma Adı;Geldiği Tarih;Fatura Tarihi;Fatura No;Seri No");
        lines[1].Should().Be("1;Amoksilin;Enjeksiyonluk çözelti;Flakon;12,5;kutu;Vet Depo;13.10.2026;12.10.2026;F-7;");
    }

    // ---- Aşı bilgi raporu ----

    private static VaccineInfoRegisterItemDto Info(bool late = false, bool voided = false) => new(
        4, Guid.NewGuid(), Moment, "Ayşe Yılmaz", null, "Pamuk", "Kedi", "900111222333444",
        "Nobivac", "LOT-1", new DateOnly(2027, 5, 31), "doz", 1m, 150m, 150m, "TRY", late, voided);

    [Fact]
    public void VaccineInfo_Csv_Should_Follow_The_Documented_Columns_And_Leave_Tc_Empty()
    {
        var lines = CsvLines(RegisterCsvWriter.WriteUtf8Bom(VaccineInfoRegisterColumns.All, [Info()]));

        lines[0].Should().Be("#;Tarih;Hasta Sahibi;T.C.;Hasta;Tür;Çip No;Ürün;Seri No;S.K.T;Birim;Miktar;Fiyat;Toplam");
        lines[1].Should().Be("4;13.10.2026;Ayşe Yılmaz;;Pamuk;Kedi;900111222333444;Nobivac;LOT-1;31.05.2027;doz;1;150;150");
    }

    [Fact]
    public void VaccineInfo_Product_Cell_Should_Carry_Late_And_Voided_Marks()
    {
        var lines = CsvLines(RegisterCsvWriter.WriteUtf8Bom(VaccineInfoRegisterColumns.All, [Info(late: true, voided: true)]));

        lines[1].Split(';')[7].Should().Be("Nobivac (sonradan eklendi) (iptal)");
    }

    // ---- Narkotik ve psikotropik defteri ----

    private static ControlledSubstanceMovement Movement(
        StockMovementType type,
        decimal quantity = 2m,
        string? reason = null,
        string? referenceType = null,
        bool batched = true,
        string? supplier = "Eczane Deposu",
        DateOnly? receivedDate = null,
        decimal? after = null,
        decimal? previous = null,
        StockMovementType? reversedType = null,
        string? target = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), type, Moment, quantity, reason, referenceType,
        "Ketamin", "Enjeksiyonluk çözelti", "Flakon", "10 ml",
        batched ? Guid.NewGuid() : null, batched ? "LOT-9" : null, batched ? supplier : null,
        batched ? "IRS-5" : null, batched ? receivedDate : null, after, previous, reversedType, target);

    [Fact]
    public void Batched_Entry_Should_Be_On_The_Received_Side_With_Batch_Place_And_Document()
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(1, Movement(StockMovementType.In, receivedDate: new DateOnly(2026, 10, 9)));

        item.Direction.Should().Be("Received");
        item.ReceivedFrom.Should().Be("Eczane Deposu");
        item.ReceivedDate.Should().Be(new DateOnly(2026, 10, 9));
        item.ReceivedDocumentNumber.Should().Be("IRS-5");
        item.IssuedTo.Should().BeNull();
        item.IssuedDate.Should().BeNull();
        item.LotNumber.Should().Be("LOT-9");
        item.CommercialPresentation.Should().Be("Flakon, 10 ml");
        item.IsBatched.Should().BeTrue();
    }

    [Fact]
    public void Entry_Without_Receipt_Date_Should_Use_The_Istanbul_Day_Of_The_Movement()
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(1, Movement(StockMovementType.In));

        item.ReceivedDate.Should().Be(MomentDay);
    }

    [Theory]
    [InlineData(StockMovementType.Initial, "Devir")]
    [InlineData(StockMovementType.In, null)]
    public void Unbatched_Entry_Should_Show_Handover_Only_For_Initial_And_Have_No_Serial_Or_Document(
        StockMovementType type, string? place)
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(1, Movement(type, batched: false));

        item.Direction.Should().Be("Received");
        item.ReceivedFrom.Should().Be(place);
        item.ReceivedDocumentNumber.Should().BeNull();
        item.LotNumber.Should().BeNull();
        item.IsBatched.Should().BeFalse();
    }

    [Fact]
    public void Administration_Out_Should_Show_Patient_And_Owner_On_The_Issued_Side()
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(
            1, Movement(StockMovementType.Out, reason: "Uygulama", referenceType: Administration.StockReferenceType, target: "Pamuk (Ayşe Yılmaz)"));

        item.Direction.Should().Be("Issued");
        item.IssuedTo.Should().Be("Uygulama: Pamuk (Ayşe Yılmaz)");
        item.IssuedDate.Should().Be(MomentDay);
        item.IssuedDocumentNumber.Should().BeNull();
        item.ReceivedFrom.Should().BeNull();
        item.ReceivedDate.Should().BeNull();
    }

    [Theory]
    [InlineData("İmha", "İmha")]
    [InlineData(null, "Stok çıkışı")]
    public void Manual_Out_Should_Use_Reason_Or_Fallback_As_The_Place(string? reason, string expected)
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(1, Movement(StockMovementType.Out, reason: reason));

        item.Direction.Should().Be("Issued");
        item.IssuedTo.Should().Be(expected);
    }

    [Fact]
    public void Reversal_Of_Out_Is_Received_And_Reversal_Of_Entry_Is_Issued()
    {
        var back = ControlledSubstanceRegisterMapper.ToItem(
            1, Movement(StockMovementType.Reversal, reason: "Uygulama iptali", reversedType: StockMovementType.Out));
        var cancelled = ControlledSubstanceRegisterMapper.ToItem(
            2, Movement(StockMovementType.Reversal, reason: "Hatalı giriş", reversedType: StockMovementType.In));

        back.Direction.Should().Be("Received");
        back.ReceivedFrom.Should().Be("Geri alma: Uygulama iptali");
        back.IssuedTo.Should().BeNull();
        cancelled.Direction.Should().Be("Issued");
        cancelled.IssuedTo.Should().Be("Geri alma: Hatalı giriş");
        cancelled.ReceivedFrom.Should().BeNull();
    }

    [Theory]
    [InlineData(8, 5, "Received", "Sayım düzeltmesi (artış)")]
    [InlineData(3, 5, "Issued", "Sayım düzeltmesi (azalış)")]
    public void Batched_Adjustment_Direction_Comes_From_The_Previous_Batch_Balance(
        int after, int previous, string direction, string place)
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(
            1, Movement(StockMovementType.Adjustment, after: after, previous: previous));

        item.Direction.Should().Be(direction);
        (item.ReceivedFrom ?? item.IssuedTo).Should().Be(place);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Adjustment_Without_Derivable_Direction_Should_Be_Undetermined_On_Both_Sides(bool batched)
    {
        var item = ControlledSubstanceRegisterMapper.ToItem(
            1, Movement(StockMovementType.Adjustment, batched: batched, after: batched ? 5m : null, previous: null));

        item.Direction.Should().Be("Undetermined");
        item.ReceivedFrom.Should().Be("Sayım düzeltmesi (yön belirlenemedi)");
        item.IssuedTo.Should().Be(item.ReceivedFrom);
        item.ReceivedDate.Should().Be(MomentDay);
        item.IssuedDate.Should().Be(MomentDay);
    }

    [Fact]
    public void ControlledSubstance_Csv_Should_Have_Grouped_Header_And_Only_One_Side_Per_Row()
    {
        var rows = new[]
        {
            ControlledSubstanceRegisterMapper.ToItem(1, Movement(StockMovementType.In, quantity: 10m, receivedDate: new DateOnly(2026, 10, 9))),
            ControlledSubstanceRegisterMapper.ToItem(2, Movement(StockMovementType.Out, quantity: 1.5m, reason: "Uygulama", referenceType: Administration.StockReferenceType, target: "Pamuk (Ayşe)")),
        };

        var lines = CsvLines(RegisterCsvWriter.WriteUtf8Bom(ControlledSubstanceRegisterColumns.All, rows));

        lines[0].Should().Be(
            "Sıra No;Ürün Adı, Ticari Adı ve Farmakolojik Şekli;Ticari Takdim Şekli;Miktar;Seri No;"
            + "SATIN/İADE/DEVİR ALINAN ÜRÜNÜN · Alındığı Yer;SATIN/İADE/DEVİR ALINAN ÜRÜNÜN · Tarih;SATIN/İADE/DEVİR ALINAN ÜRÜNÜN · Belge No;"
            + "SATILAN/İADE VEYA DEVREDİLEN ÜRÜNÜN · Çıkış Yapılan Yer;SATILAN/İADE VEYA DEVREDİLEN ÜRÜNÜN · Tarih;SATILAN/İADE VEYA DEVREDİLEN ÜRÜNÜN · Reçete/Belge No");
        lines[1].Should().Be("1;Ketamin (Enjeksiyonluk çözelti);Flakon, 10 ml;10;LOT-9;Eczane Deposu;09.10.2026;IRS-5;;;");
        lines[2].Should().Be("2;Ketamin (Enjeksiyonluk çözelti);Flakon, 10 ml;1,5;LOT-9;;;;Uygulama: Pamuk (Ayşe);13.10.2026;");
        lines.Should().HaveCount(3);
    }

    [Fact]
    public void ControlledSubstance_Xlsx_Should_Merge_Group_Headers()
    {
        var bytes = RegisterXlsxWriter.WriteWorkbook(
            ControlledSubstanceRegisterColumns.Export.SheetName, ControlledSubstanceRegisterColumns.All,
            [ControlledSubstanceRegisterMapper.ToItem(1, Movement(StockMovementType.In))]);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        ws.Cell(1, 6).GetString().Should().Be("SATIN/İADE/DEVİR ALINAN ÜRÜNÜN");
        ws.Cell(1, 6).IsMerged().Should().BeTrue();
        ws.Cell(2, 6).GetString().Should().Be("Alındığı Yer");
        ws.Cell(1, 9).GetString().Should().Be("SATILAN/İADE VEYA DEVREDİLEN ÜRÜNÜN");
        ws.Cell(3, 7).GetDateTime().Should().Be(new DateTime(2026, 10, 13));
        ws.Cell(3, 10).IsEmpty().Should().BeTrue();
    }

    // ---- Ortak akış ----

    [Fact]
    public async Task Export_Should_Honour_Format_File_Name_And_Header_Only_When_Empty()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var clinic = new Mock<IClinicContext>();
        clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        var reader = new Mock<IRegisterReader<VaccineRegisterItemDto>>();
        reader
            .Setup(r => r.ReadAsync(It.IsAny<RegisterReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterReadResult<VaccineRegisterItemDto>(0, []));
        var runner = new RegisterReportRunner(tenant.Object, clinic.Object, ClinicReadScopeResolverMock.Default().Object);
        var query = new RegisterQuery(MomentDay, MomentDay, null, false);

        var csv = await new ExportVaccineRegisterQueryHandler(runner, reader.Object)
            .Handle(new ExportVaccineRegisterQuery(query, RegisterExportFormat.Csv), CancellationToken.None);
        var xlsx = await new ExportVaccineRegisterQueryHandler(runner, reader.Object)
            .Handle(new ExportVaccineRegisterQuery(query, RegisterExportFormat.Xlsx), CancellationToken.None);

        csv.Value!.FileDownloadName.Should().MatchRegex(@"^asi-kayit-defteri-\d{8}\.csv$");
        CsvLines(csv.Value.Content).Should().HaveCount(1);
        xlsx.Value!.FileDownloadName.Should().MatchRegex(@"^asi-kayit-defteri-\d{8}\.xlsx$");
        using var wb = new XLWorkbook(new MemoryStream(xlsx.Value.Content));
        wb.Worksheet(1).LastRowUsed()!.RowNumber().Should().Be(1);
    }
}
