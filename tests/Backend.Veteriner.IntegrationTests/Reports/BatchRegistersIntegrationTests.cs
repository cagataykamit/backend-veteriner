using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;
using Backend.Veteriner.Application.Reports.Registers.Medications;
using Backend.Veteriner.Application.Reports.Registers.VaccineInfo;
using Backend.Veteriner.Application.Reports.Registers.Vaccines;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Backend.Veteriner.Domain.Visits;
using Backend.Veteriner.Infrastructure.Persistence;
using ClosedXML.Excel;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Reports;

/// <summary>
/// Parti bazlı defterler uçtan uca (HTTP + gerçek SQL Server, her test kendi kiracısı): aşı kayıt defteri, ilaç kayıt defteri,
/// narkotik/psikotropik stok ve sarf defteri, aşı bilgi raporu. Kanıtlananlar: ürün sınıfı süzgeci, İstanbul gün sınırı,
/// sıra no + sayfalama, partisiz/geri alınan giriş, narkotik taraf kuralları, ekran = CSV = XLSX, klinik kapsamı, IDOR, yetki kombinasyonu.
/// </summary>
[Collection("pilot-smoke-api")]
public sealed class BatchRegistersIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string BaseUrl = "/api/v1/reports/registers";
    private const string Vaccines = $"{BaseUrl}/vaccines";
    private const string Medications = $"{BaseUrl}/medications";
    private const string Controlled = $"{BaseUrl}/controlled-substances";
    private const string VaccineInfo = $"{BaseUrl}/vaccine-info";

    // Europe/Istanbul = UTC+3 (kalıcı): 10 Mart 00:00 İstanbul = 9 Mart 21:00 UTC.
    private static readonly DateTime DayStartUtc = new(2026, 3, 9, 21, 0, 0, DateTimeKind.Utc);
    private const string Day = "2026-03-10";

    private static readonly string[] AllReadPermissions =
    [
        PermissionCatalog.StockMovements.Read,
        PermissionCatalog.Administrations.Read,
        PermissionCatalog.Vaccinations.Read,
        PermissionCatalog.Products.Read,
    ];

    private static int _phoneSeed = 7_000_000;

    private readonly CustomWebApplicationFactory _factory;

    public BatchRegistersIntegrationTests(CustomWebApplicationFactory factory) => _factory = factory;

    // ---- Aşı kayıt defteri ----

    [Fact]
    public async Task Vaccine_Register_Should_List_Only_Vaccine_Entries_With_Batch_Fields_And_Handle_Unbatched_And_Reversed()
    {
        var env = await SeedTenantAsync();
        var vaccine = await env.SeedProductAsync("Nobivac", ProductKind.Vaccine);
        var medicine = await env.SeedProductAsync("Amoksilin", ProductKind.Medicine);
        var other = await env.SeedProductAsync("Gazli bez", ProductKind.Other);
        var batched = await env.SeedEntryAsync(vaccine, 20m, DayStartUtc.AddHours(9), new StockBatchDetails(
            "L1", new DateOnly(2027, 5, 31), "Vet Depo", "F-1", new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 9),
            new DateOnly(2026, 3, 7), "Soguk zincir araci", "Indikator X"));
        var legacy = await env.SeedEntryAsync(vaccine, 5m, DayStartUtc.AddHours(10), details: null);
        var cancelled = await env.SeedEntryAsync(vaccine, 3m, DayStartUtc.AddHours(11), new StockBatchDetails("L2", new DateOnly(2027, 1, 1)));
        await env.SeedReversalAsync(cancelled.MovementId, DayStartUtc.AddHours(12), "Hatali giris");
        await env.SeedEntryAsync(medicine, 7m, DayStartUtc.AddHours(9), details: null);
        await env.SeedEntryAsync(other, 7m, DayStartUtc.AddHours(9), details: null);

        var shown = await env.GetAsync<VaccineRegisterItemDto>(Vaccines);
        var withCancelled = await env.GetAsync<VaccineRegisterItemDto>(Vaccines, "&includeVoided=true");

        shown.TotalCount.Should().Be(2);
        shown.Items.Select(i => i.MovementId).Should().Equal(batched.MovementId, legacy.MovementId);
        var row = shown.Items[0];
        row.RowNumber.Should().Be(1);
        row.TradeName.Should().Be("Nobivac");
        row.LotNumber.Should().Be("L1");
        row.ExpiryDate.Should().Be(new DateOnly(2027, 5, 31));
        row.TotalQuantity.Should().Be(20m);
        row.SupplierName.Should().Be("Vet Depo");
        row.TransportMethod.Should().Be("Soguk zincir araci");
        row.ColdChainMonitor.Should().Be("Indikator X");
        row.DispatchedDate.Should().Be(new DateOnly(2026, 3, 7));
        row.DeliveredDate.Should().Be(new DateOnly(2026, 3, 9));
        row.InvoiceDate.Should().Be(new DateOnly(2026, 3, 8));
        row.InvoiceNumber.Should().Be("F-1");
        (row.IsBatched, row.IsReversed).Should().Be((true, false));

        var old = shown.Items[1];
        old.LotNumber.Should().BeNull();
        old.ExpiryDate.Should().BeNull();
        old.SupplierName.Should().BeNull();
        old.DeliveredDate.Should().Be(new DateOnly(2026, 3, 10), "geliş tarihi yoksa girişin İstanbul günü");
        (old.IsBatched, old.IsReversed).Should().Be((false, false));

        withCancelled.Items.Select(i => i.MovementId).Should().Equal(batched.MovementId, legacy.MovementId, cancelled.MovementId);
        withCancelled.Items[2].IsReversed.Should().BeTrue();
    }

    [Fact]
    public async Task Stock_Registers_Should_Use_Istanbul_Day_Boundaries_Number_Rows_And_Page_Without_Overlap()
    {
        var env = await SeedTenantAsync();
        var vaccine = await env.SeedProductAsync("Sinir Asi", ProductKind.Vaccine);
        var details = new StockBatchDetails("L", new DateOnly(2027, 1, 1));
        await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddSeconds(-1), details);
        var first = await env.SeedEntryAsync(vaccine, 1m, DayStartUtc, details);
        var middle = await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddHours(5), details);
        var last = await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddDays(1).AddSeconds(-1), details);
        await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddDays(1), details);

        var all = await env.GetAsync<VaccineRegisterItemDto>(Vaccines);
        var page1 = await env.GetAsync<VaccineRegisterItemDto>(Vaccines, "&page=1&pageSize=2");
        var page2 = await env.GetAsync<VaccineRegisterItemDto>(Vaccines, "&page=2&pageSize=2");

        all.Items.Select(i => i.MovementId).Should().Equal(first.MovementId, middle.MovementId, last.MovementId);
        page1.Items.Select(i => i.MovementId).Should().Equal(first.MovementId, middle.MovementId);
        page2.Items.Select(i => i.MovementId).Should().Equal(last.MovementId);
        page1.Items.Concat(page2.Items).Select(i => i.RowNumber).Should().Equal(1, 2, 3);
        new[] { all, page1, page2 }.Should().OnlyContain(p => p.TotalCount == 3);
    }

    // ---- İlaç kayıt defteri ----

    [Fact]
    public async Task Medication_Register_Should_List_Only_Medicine_Entries_And_Include_Controlled_Medicine()
    {
        var env = await SeedTenantAsync();
        var plain = await env.SeedProductAsync("Amoksilin", ProductKind.Medicine, pharmacologicalForm: "Tablet", presentationForm: "Blister", unit: "kutu");
        var controlled = await env.SeedProductAsync("Ketamin", ProductKind.Medicine, ControlledSubstanceClass.Narcotic);
        var vaccine = await env.SeedProductAsync("Asi", ProductKind.Vaccine);
        var other = await env.SeedProductAsync("Bez", ProductKind.Other);
        var entry = await env.SeedEntryAsync(plain, 12.5m, DayStartUtc.AddHours(9), new StockBatchDetails(
            LotNumber: "AM-1", SupplierName: "Eczane", InvoiceNumber: "F-7", InvoiceDate: new DateOnly(2026, 3, 8), ReceivedDate: new DateOnly(2026, 3, 9)));
        var noLot = await env.SeedEntryAsync(plain, 3m, DayStartUtc.AddHours(10), details: null);
        var narcotic = await env.SeedEntryAsync(controlled, 4m, DayStartUtc.AddHours(11), new StockBatchDetails("K-1", new DateOnly(2027, 1, 1)));
        await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddHours(9), new StockBatchDetails("V", new DateOnly(2027, 1, 1)));
        await env.SeedEntryAsync(other, 1m, DayStartUtc.AddHours(9), details: null);

        var result = await env.GetAsync<MedicationRegisterItemDto>(Medications);

        result.Items.Select(i => i.MovementId).Should().Equal(entry.MovementId, noLot.MovementId, narcotic.MovementId);
        var row = result.Items[0];
        row.ProductName.Should().Be("Amoksilin");
        row.PharmacologicalForm.Should().Be("Tablet");
        row.PresentationForm.Should().Be("Blister");
        row.Quantity.Should().Be(12.5m);
        row.Unit.Should().Be("kutu");
        row.SupplierName.Should().Be("Eczane");
        row.ReceivedDate.Should().Be(new DateOnly(2026, 3, 9));
        row.InvoiceDate.Should().Be(new DateOnly(2026, 3, 8));
        row.InvoiceNumber.Should().Be("F-7");
        row.LotNumber.Should().Be("AM-1");
        result.Items[1].IsBatched.Should().BeFalse();
        result.Items[1].ReceivedDate.Should().Be(new DateOnly(2026, 3, 10));
    }

    // ---- Narkotik ve psikotropik defteri ----

    [Fact]
    public async Task Controlled_Register_Should_Show_One_Row_Per_Movement_With_Received_Or_Issued_Side()
    {
        var env = await SeedTenantAsync();
        var client = await env.SeedClientAsync("Ayse Yilmaz");
        var pet = await env.SeedPetAsync(client, "Pamuk");
        var ketamine = await env.SeedProductAsync(
            "Ketamin", ProductKind.Medicine, ControlledSubstanceClass.Narcotic,
            pharmacologicalForm: "Enjeksiyonluk cozelti", presentationForm: "Flakon", packaging: "10 ml");
        var diazepam = await env.SeedProductAsync("Diazepam", ProductKind.Other, ControlledSubstanceClass.Psychotropic);
        var plain = await env.SeedProductAsync("Serum", ProductKind.Other);

        var entry = await env.SeedEntryAsync(ketamine, 10m, DayStartUtc.AddHours(1), new StockBatchDetails(
            "K-1", new DateOnly(2027, 1, 1), "Eczane Deposu", "IRS-5", ReceivedDate: new DateOnly(2026, 3, 9)));
        var administrationId = await env.SeedAdministrationAsync(pet, ketamine, entry.BatchId, 1m, DayStartUtc.AddHours(2), AdministrationFlags.None);
        var administered = await env.SeedOutAsync(ketamine, entry.BatchId, 1m, DayStartUtc.AddHours(2), "Uygulama", Administration.StockReferenceType, administrationId);
        var disposed = await env.SeedOutAsync(ketamine, entry.BatchId, 2m, DayStartUtc.AddHours(3), "Imha");
        var reversed = await env.SeedReversalAsync(disposed.MovementId, DayStartUtc.AddHours(4), "Hatali imha");
        var decrease = await env.SeedAdjustmentAsync(ketamine, entry.BatchId!.Value, 7m, DayStartUtc.AddHours(5));
        var increase = await env.SeedAdjustmentAsync(ketamine, entry.BatchId!.Value, 8m, DayStartUtc.AddHours(6));
        var legacy = await env.SeedEntryAsync(diazepam, 30m, DayStartUtc.AddHours(7), details: null, StockMovementType.Initial);
        var undetermined = await env.SeedAdjustmentAsync(diazepam, null, 25m, DayStartUtc.AddHours(8));
        await env.SeedEntryAsync(plain, 5m, DayStartUtc.AddHours(1), details: null);

        var result = await env.GetAsync<ControlledSubstanceRegisterItemDto>(Controlled);

        result.TotalCount.Should().Be(8);
        result.Items.Select(i => i.MovementId).Should().Equal(
            entry.MovementId, administered.MovementId, disposed.MovementId, reversed, decrease, increase, legacy.MovementId, undetermined);
        result.Items.Select(i => i.RowNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        result.Items.Select(i => i.Direction).Should().Equal(
            "Received", "Issued", "Issued", "Received", "Issued", "Received", "Received", "Undetermined");

        var received = result.Items[0];
        received.ProductName.Should().Be("Ketamin");
        received.PharmacologicalForm.Should().Be("Enjeksiyonluk cozelti");
        received.CommercialPresentation.Should().Be("Flakon, 10 ml");
        received.LotNumber.Should().Be("K-1");
        received.Quantity.Should().Be(10m);
        (received.ReceivedFrom, received.ReceivedDate, received.ReceivedDocumentNumber)
            .Should().Be(("Eczane Deposu", new DateOnly(2026, 3, 9), "IRS-5"));
        (received.IssuedTo, received.IssuedDate, received.IssuedDocumentNumber).Should().Be((null, null, null));

        var sale = result.Items[1];
        (sale.IssuedTo, sale.IssuedDate).Should().Be(("Uygulama: Pamuk (Ayse Yilmaz)", new DateOnly(2026, 3, 10)));
        sale.ReceivedFrom.Should().BeNull();
        result.Items[2].IssuedTo.Should().Be("Imha");
        result.Items[3].ReceivedFrom.Should().Be("Geri alma: Hatali imha");
        result.Items[4].IssuedTo.Should().Be("Sayım düzeltmesi (azalış)");
        result.Items[4].Quantity.Should().Be(2m);
        result.Items[5].ReceivedFrom.Should().Be("Sayım düzeltmesi (artış)");
        result.Items[5].Quantity.Should().Be(1m);

        var handover = result.Items[6];
        (handover.ReceivedFrom, handover.LotNumber, handover.IsBatched).Should().Be(("Devir", null, false));
        var unknown = result.Items[7];
        unknown.ReceivedFrom.Should().Be("Sayım düzeltmesi (yön belirlenemedi)");
        unknown.IssuedTo.Should().Be(unknown.ReceivedFrom);
        result.Items.Should().NotContain(i => i.ProductName == "Serum");
    }

    // ---- Aşı bilgi raporu ----

    [Fact]
    public async Task VaccineInfo_Should_List_Vaccine_Administrations_With_Batch_Price_Flags_And_Filters()
    {
        var env = await SeedTenantAsync();
        var otherVet = await env.SeedUserAsync();
        var client = await env.SeedClientAsync("Ayse Yilmaz");
        var pet = await env.SeedPetAsync(client, "Pamuk");
        var vaccine = await env.SeedProductAsync("Nobivac", ProductKind.Vaccine, unit: "doz", unitPrice: 150m);
        var priced = await env.SeedProductAsync("Rabisin", ProductKind.Vaccine, unit: "doz", unitPrice: 33.33m);
        var medicine = await env.SeedProductAsync("Amoksilin", ProductKind.Medicine);
        var entry = await env.SeedEntryAsync(vaccine, 10m, DayStartUtc.AddHours(1), new StockBatchDetails("L1", new DateOnly(2027, 5, 31)));
        var a1 = await env.SeedAdministrationAsync(pet, vaccine, entry.BatchId, 1m, DayStartUtc.AddHours(9), AdministrationFlags.None);
        var a2 = await env.SeedAdministrationAsync(pet, vaccine, null, 2m, DayStartUtc.AddHours(10), AdministrationFlags.Late);
        var a3 = await env.SeedAdministrationAsync(pet, vaccine, entry.BatchId, 1m, DayStartUtc.AddHours(11), AdministrationFlags.Voided);
        var a4 = await env.SeedAdministrationAsync(pet, priced, null, 3m, DayStartUtc.AddHours(12), AdministrationFlags.None, responsibleVeterinarian: otherVet);
        var a5 = await env.SeedAdministrationAsync(pet, priced, null, 1m, DayStartUtc.AddHours(13), AdministrationFlags.None, createdBy: otherVet);
        await env.SeedAdministrationAsync(pet, medicine, null, 1m, DayStartUtc.AddHours(9), AdministrationFlags.None);

        var shown = await env.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo);
        var withVoided = await env.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo, "&includeVoided=true");
        var byProduct = await env.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo, $"&productId={vaccine}");
        var byVet = await env.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo, $"&veterinarianId={otherVet}");

        shown.Items.Select(i => i.AdministrationId).Should().Equal(a1, a2, a4, a5);
        withVoided.Items.Select(i => i.AdministrationId).Should().Equal(a1, a2, a3, a4, a5);
        withVoided.Items[2].IsVoided.Should().BeTrue();
        byProduct.Items.Select(i => i.AdministrationId).Should().Equal(a1, a2);
        byVet.Items.Select(i => i.AdministrationId).Should().Equal(a4);

        var row = shown.Items[0];
        row.RowNumber.Should().Be(1);
        row.OwnerName.Should().Be("Ayse Yilmaz");
        row.ClientIdentityNumber.Should().BeNull();
        row.PetName.Should().Be("Pamuk");
        row.Species.Should().NotBeNullOrEmpty();
        row.MicrochipNumber.Should().Be("900111222333444");
        row.ProductName.Should().Be("Nobivac");
        row.LotNumber.Should().Be("L1");
        row.ExpiryDate.Should().Be(new DateOnly(2027, 5, 31));
        (row.Unit, row.Quantity, row.UnitPrice, row.TotalPrice, row.Currency).Should().Be(("doz", 1m, 150m, 150m, "TRY"));
        (row.AddedAfterFinalization, row.IsVoided).Should().Be((false, false));

        var legacy = shown.Items[1];
        (legacy.LotNumber, legacy.ExpiryDate, legacy.AddedAfterFinalization).Should().Be((null, null, true));
        shown.Items[2].TotalPrice.Should().Be(99.99m, "3 × 33,33");
    }

    // ---- Ekran = CSV = XLSX ----

    [Fact]
    public async Task Vaccine_And_Medication_Screen_Csv_And_Xlsx_Should_Match()
    {
        var env = await SeedTenantAsync();
        var vaccine = await env.SeedProductAsync("Nobivac", ProductKind.Vaccine);
        var medicine = await env.SeedProductAsync("Amoksilin", ProductKind.Medicine, pharmacologicalForm: "Tablet", unit: "kutu");
        await env.SeedEntryAsync(vaccine, 20m, DayStartUtc.AddHours(2), new StockBatchDetails("L1", new DateOnly(2027, 5, 31), "Depo", "F-1"));
        await env.SeedEntryAsync(vaccine, 4m, DayStartUtc.AddHours(3), details: null);
        await env.SeedEntryAsync(medicine, 6m, DayStartUtc.AddHours(2), new StockBatchDetails(LotNumber: "M1", SupplierName: "Eczane"));

        var vaccineScreen = await env.GetAsync<VaccineRegisterItemDto>(Vaccines);
        var vaccineCsv = await env.GetCsvAsync(Vaccines);
        var vaccineXlsx = await env.GetXlsxAsync(Vaccines, headerRows: 1);
        vaccineCsv.Select(r => r[0]).Should().Equal(vaccineScreen.Items.Select(i => i.RowNumber.ToString(CultureInfo.InvariantCulture)));
        vaccineCsv.Select(r => r[1]).Should().Equal(vaccineScreen.Items.Select(i => i.TradeName));
        vaccineCsv.Select(r => r[2]).Should().Equal(vaccineScreen.Items.Select(i => i.LotNumber ?? string.Empty));
        vaccineCsv.Select(r => r[3]).Should().Equal("31.05.2027", string.Empty);
        vaccineCsv.Select(r => r[9]).Should().Equal("10.03.2026", "10.03.2026");
        vaccineXlsx.Select(r => (int)r.Cell(1).GetDouble()).Should().Equal(vaccineScreen.Items.Select(i => i.RowNumber));
        vaccineXlsx.Select(r => r.Cell(3).GetString()).Should().Equal(vaccineScreen.Items.Select(i => i.LotNumber ?? string.Empty));
        vaccineXlsx[0].Cell(4).GetDateTime().Should().Be(new DateTime(2027, 5, 31));
        vaccineXlsx[1].Cell(4).IsEmpty().Should().BeTrue();

        var medicationScreen = await env.GetAsync<MedicationRegisterItemDto>(Medications);
        var medicationCsv = await env.GetCsvAsync(Medications);
        var medicationXlsx = await env.GetXlsxAsync(Medications, headerRows: 1);
        medicationCsv.Should().ContainSingle();
        medicationCsv[0][1].Should().Be(medicationScreen.Items[0].ProductName);
        medicationCsv[0][2].Should().Be("Tablet");
        medicationCsv[0][4].Should().Be("6");
        medicationCsv[0][5].Should().Be("kutu");
        medicationCsv[0][10].Should().Be("M1");
        medicationXlsx.Should().ContainSingle();
        medicationXlsx[0].Cell(5).GetDouble().Should().Be(6);
        medicationXlsx[0].Cell(8).GetDateTime().Should().Be(new DateTime(2026, 3, 10));
    }

    [Fact]
    public async Task Controlled_And_VaccineInfo_Screen_Csv_And_Xlsx_Should_Match()
    {
        var env = await SeedTenantAsync();
        var pet = await env.SeedPetAsync(await env.SeedClientAsync("Ayse Yilmaz"), "Pamuk");
        var ketamine = await env.SeedProductAsync("Ketamin", ProductKind.Medicine, ControlledSubstanceClass.Narcotic, pharmacologicalForm: "Cozelti");
        var vaccine = await env.SeedProductAsync("Nobivac", ProductKind.Vaccine, unit: "doz", unitPrice: 150m);
        var entry = await env.SeedEntryAsync(ketamine, 10m, DayStartUtc.AddHours(1), new StockBatchDetails(
            "K-1", new DateOnly(2027, 1, 1), "Depo", "IRS-5", ReceivedDate: new DateOnly(2026, 3, 9)));
        await env.SeedOutAsync(ketamine, entry.BatchId, 1.5m, DayStartUtc.AddHours(2), "Imha");
        await env.SeedAdministrationAsync(pet, vaccine, null, 2m, DayStartUtc.AddHours(3), AdministrationFlags.Late);

        var controlledScreen = await env.GetAsync<ControlledSubstanceRegisterItemDto>(Controlled);
        var controlledCsv = await env.GetCsvAsync(Controlled);
        var controlledXlsx = await env.GetXlsxAsync(Controlled, headerRows: 2);
        controlledCsv.Select(r => r[0]).Should().Equal("1", "2");
        controlledCsv.Select(r => r[1]).Should().Equal("Ketamin (Cozelti)", "Ketamin (Cozelti)");
        controlledCsv.Select(r => r[3]).Should().Equal("10", "1,5");
        controlledCsv[0][5..8].Should().Equal("Depo", "09.03.2026", "IRS-5");
        controlledCsv[0][8..11].Should().Equal(string.Empty, string.Empty, string.Empty);
        controlledCsv[1][5..8].Should().Equal(string.Empty, string.Empty, string.Empty);
        controlledCsv[1][8..11].Should().Equal("Imha", "10.03.2026", string.Empty);
        controlledScreen.Items.Select(i => i.Quantity).Should().Equal(10m, 1.5m);
        controlledXlsx.Select(r => r.Cell(4).GetDouble()).Should().Equal(10d, 1.5d);
        controlledXlsx[0].Cell(7).GetDateTime().Should().Be(new DateTime(2026, 3, 9));
        controlledXlsx[1].Cell(9).GetString().Should().Be("Imha");

        var infoScreen = await env.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo);
        var infoCsv = await env.GetCsvAsync(VaccineInfo);
        var infoXlsx = await env.GetXlsxAsync(VaccineInfo, headerRows: 1);
        infoCsv.Should().ContainSingle();
        infoCsv[0].Should().Equal("1", "10.03.2026", "Ayse Yilmaz", string.Empty, "Pamuk", infoScreen.Items[0].Species,
            "900111222333444", "Nobivac (sonradan eklendi)", string.Empty, string.Empty, "doz", "2", "150", "300");
        infoXlsx[0].Cell(12).GetDouble().Should().Be(2);
        infoXlsx[0].Cell(14).GetDouble().Should().Be(300);
        infoXlsx[0].Cell(2).GetDateTime().Should().Be(new DateTime(2026, 3, 10));
    }

    // ---- Kapsam, IDOR, yetki, doğrulama ----

    [Fact]
    public async Task Batch_Registers_Should_Only_Show_Assigned_Clinics_And_Reject_Foreign_Clinic()
    {
        var env = await SeedTenantAsync();
        var clinicB = await env.SeedClinicAsync();
        var pet = await env.SeedPetAsync(await env.SeedClientAsync("Klinik Test"), "Klinik");
        var vaccine = await env.SeedProductAsync("Asi", ProductKind.Vaccine);
        var ketamine = await env.SeedProductAsync("Ketamin", ProductKind.Medicine, ControlledSubstanceClass.Narcotic);
        var details = new StockBatchDetails("L", new DateOnly(2027, 1, 1));
        var mine = await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddHours(2), details);
        await env.SeedEntryAsync(vaccine, 1m, DayStartUtc.AddHours(2), details, clinicId: clinicB);
        var myKetamine = await env.SeedEntryAsync(ketamine, 1m, DayStartUtc.AddHours(2), details);
        await env.SeedEntryAsync(ketamine, 1m, DayStartUtc.AddHours(2), details, clinicId: clinicB);
        var myAdministration = await env.SeedAdministrationAsync(pet, vaccine, null, 1m, DayStartUtc.AddHours(2), AdministrationFlags.None);
        await env.SeedAdministrationAsync(pet, vaccine, null, 1m, DayStartUtc.AddHours(2), AdministrationFlags.None, clinicId: clinicB);

        (await env.GetAsync<VaccineRegisterItemDto>(Vaccines)).Items.Select(i => i.MovementId).Should().Equal(mine.MovementId);
        (await env.GetAsync<ControlledSubstanceRegisterItemDto>(Controlled)).Items.Select(i => i.MovementId).Should().Equal(myKetamine.MovementId);
        (await env.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo)).Items.Select(i => i.AdministrationId).Should().Equal(myAdministration);
        (await env.GetAsync<VaccineRegisterItemDto>(Vaccines, $"&clinicId={env.ClinicId}")).TotalCount.Should().Be(1);

        foreach (var url in AllUrls())
        {
            var foreign = await env.Http.GetAsync($"{url}?from={Day}&to={Day}&clinicId={clinicB}");
            foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(foreign)).Should().Be("Clinics.AccessDenied", url);
        }
    }

    [Fact]
    public async Task Batch_Registers_Should_Not_Expose_Other_Tenant_Records()
    {
        var one = await SeedTenantAsync();
        var two = await SeedTenantAsync();
        var petOne = await one.SeedPetAsync(await one.SeedClientAsync("Birinci Sahip"), "Birinci");
        var petTwo = await two.SeedPetAsync(await two.SeedClientAsync("Ikinci Sahip"), "Ikinci");
        var vaccineOne = await one.SeedProductAsync("Asi Bir", ProductKind.Vaccine);
        var vaccineTwo = await two.SeedProductAsync("Asi Iki", ProductKind.Vaccine);
        var ketamineOne = await one.SeedProductAsync("Ketamin Bir", ProductKind.Medicine, ControlledSubstanceClass.Narcotic);
        var ketamineTwo = await two.SeedProductAsync("Ketamin Iki", ProductKind.Medicine, ControlledSubstanceClass.Narcotic);
        var details = new StockBatchDetails("L", new DateOnly(2027, 1, 1));
        var entryOne = await one.SeedEntryAsync(vaccineOne, 1m, DayStartUtc.AddHours(2), details);
        var entryTwo = await two.SeedEntryAsync(vaccineTwo, 1m, DayStartUtc.AddHours(2), details);
        var controlledOne = await one.SeedEntryAsync(ketamineOne, 1m, DayStartUtc.AddHours(2), details);
        var controlledTwo = await two.SeedEntryAsync(ketamineTwo, 1m, DayStartUtc.AddHours(2), details);
        var administrationOne = await one.SeedAdministrationAsync(petOne, vaccineOne, null, 1m, DayStartUtc.AddHours(2), AdministrationFlags.None);
        var administrationTwo = await two.SeedAdministrationAsync(petTwo, vaccineTwo, null, 1m, DayStartUtc.AddHours(2), AdministrationFlags.None);

        (await one.GetAsync<VaccineRegisterItemDto>(Vaccines)).Items.Select(i => i.MovementId).Should().Equal(entryOne.MovementId);
        (await two.GetAsync<VaccineRegisterItemDto>(Vaccines)).Items.Select(i => i.MovementId).Should().Equal(entryTwo.MovementId);
        (await one.GetAsync<ControlledSubstanceRegisterItemDto>(Controlled)).Items.Select(i => i.MovementId).Should().Equal(controlledOne.MovementId);
        (await two.GetAsync<ControlledSubstanceRegisterItemDto>(Controlled)).Items.Select(i => i.MovementId).Should().Equal(controlledTwo.MovementId);
        (await one.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo)).Items.Select(i => i.AdministrationId).Should().Equal(administrationOne);
        (await two.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo)).Items.Select(i => i.AdministrationId).Should().Equal(administrationTwo);

        // Başka kiracının ürün filtresi hata vermez, satır da getirmez.
        (await one.GetAsync<VaccineInfoRegisterItemDto>(VaccineInfo, $"&productId={vaccineTwo}")).TotalCount.Should().Be(0);

        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            foreach (var url in new[] { Vaccines, Medications, Controlled, VaccineInfo })
            {
                var cross = await one.Http.GetAsync($"{url}{suffix}?from={Day}&to={Day}&clinicId={two.ClinicId}");
                cross.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
            }
        }
    }

    [Fact]
    public async Task Each_Register_Needs_Its_Full_Permission_Set_And_A_Token()
    {
        var env = await SeedTenantAsync();
        var anonymous = _factory.CreateClient();
        var stockOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.StockMovements.Read]);
        var administrationsOnly = await env.NewHttpForAnotherUserAsync([PermissionCatalog.Administrations.Read]);
        var stockAndAdministrations = await env.NewHttpForAnotherUserAsync(
            [PermissionCatalog.StockMovements.Read, PermissionCatalog.Administrations.Read]);
        var infoMissingProducts = await env.NewHttpForAnotherUserAsync(
            [PermissionCatalog.Administrations.Read, PermissionCatalog.Vaccinations.Read]);
        var infoFull = await env.NewHttpForAnotherUserAsync(
            [PermissionCatalog.Administrations.Read, PermissionCatalog.Vaccinations.Read, PermissionCatalog.Products.Read]);
        var query = $"?from={Day}&to={Day}";

        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            foreach (var url in new[] { Vaccines, Medications, Controlled, VaccineInfo })
                (await anonymous.GetAsync($"{url}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);

            // Aşı/ilaç: yalnız StockMovements.Read yeter.
            foreach (var url in new[] { Vaccines, Medications })
            {
                (await stockOnly.GetAsync($"{url}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.OK, url);
                (await administrationsOnly.GetAsync($"{url}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            }

            // Narkotik: StockMovements.Read VE Administrations.Read birlikte.
            (await stockOnly.GetAsync($"{Controlled}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await administrationsOnly.GetAsync($"{Controlled}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await stockAndAdministrations.GetAsync($"{Controlled}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.OK);

            // Aşı bilgi: üç izin birlikte.
            (await stockAndAdministrations.GetAsync($"{VaccineInfo}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await infoMissingProducts.GetAsync($"{VaccineInfo}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await infoFull.GetAsync($"{VaccineInfo}{suffix}{query}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Batch_Registers_Should_Return_400_For_Invalid_Date_Ranges()
    {
        var env = await SeedTenantAsync();

        foreach (var suffix in new[] { "", "/export", "/export-xlsx" })
        {
            foreach (var url in new[] { Vaccines, Medications, Controlled, VaccineInfo })
            {
                await ExpectAsync(env.Http, $"{url}{suffix}", "Registers.DateRangeInvalid");
                await ExpectAsync(env.Http, $"{url}{suffix}?from=2026-03-11&to=2026-03-10", "Registers.DateRangeInvalid");
                await ExpectAsync(env.Http, $"{url}{suffix}?from=2025-01-01&to=2026-03-10", "Registers.RangeTooLong");
            }
        }

        static async Task ExpectAsync(HttpClient http, string url, string code)
        {
            var response = await http.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
            (await IntegrationTestProblemDetails.ReadCodeAsync(response)).Should().Be(code, url);
        }
    }

    private static string[] AllUrls()
        => new[] { Vaccines, Medications, Controlled, VaccineInfo }
            .SelectMany(u => new[] { u, $"{u}/export", $"{u}/export-xlsx" }).ToArray();

    // ---- Kurulum ----

    private async Task<Env> SeedTenantAsync()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        await IntegrationTestAuthHelper.EnsureRolePermissionBindingsAsync(_factory.Services);

        Guid tenantId;
        Guid clinicId;
        Guid userId;
        string email;
        await using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant($"Bre-{Guid.NewGuid():N}"[..18], DateTime.UtcNow);
            db.Tenants.Add(tenant);
            db.TenantSubscriptions.Add(TenantSubscription.StartTrial(tenant.Id, SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));
            var clinic = new Clinic(tenant.Id, $"Bre-{Guid.NewGuid():N}"[..14], "Izmir", address: "Klinik Adres 1");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();

            (userId, email) = await Env.CreateUserAsync(db, hasher, tenant.Id, clinic.Id);
            tenantId = tenant.Id;
            clinicId = clinic.Id;
        }

        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_factory.Services, email, AllReadPermissions);
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new Env(_factory.Services, hasher, _factory.CreateClient, http, tenantId, clinicId, userId);
    }

    [Flags]
    private enum AdministrationFlags
    {
        None = 0,
        Late = 1,
        Voided = 2,
    }

    /// <summary>Girişin hareket ve (varsa) parti kimliği.</summary>
    private sealed record Entry(Guid MovementId, Guid? BatchId);

    private sealed class Env
    {
        private readonly IServiceProvider _services;
        private readonly IPasswordHasher _hasher;
        private readonly Func<HttpClient> _createClient;

        public Env(IServiceProvider services, IPasswordHasher hasher, Func<HttpClient> createClient, HttpClient http, Guid tenantId, Guid clinicId, Guid userId)
        {
            _services = services;
            _hasher = hasher;
            _createClient = createClient;
            Http = http;
            TenantId = tenantId;
            ClinicId = clinicId;
            UserId = userId;
        }

        public HttpClient Http { get; }
        public Guid TenantId { get; }
        public Guid ClinicId { get; }
        public Guid UserId { get; }

        public static async Task<(Guid UserId, string Email)> CreateUserAsync(AppDbContext db, IPasswordHasher hasher, Guid tenantId, Guid clinicId)
        {
            var claim = await db.OperationClaims.SingleAsync(c => c.Name == "ClinicAdmin");
            var email = $"bre-user-{Guid.NewGuid():N}@example.com";
            var user = new User(email, hasher.Hash("123456"), DateTime.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            db.UserOperationClaims.Add(new UserOperationClaim(user.Id, claim.Id));
            db.UserTenants.Add(new UserTenant(user.Id, tenantId, DateTime.UtcNow));
            db.UserClinics.Add(new UserClinic(user.Id, clinicId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            return (user.Id, email);
        }

        public async Task<Guid> SeedUserAsync()
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return (await CreateUserAsync(db, _hasher, TenantId, ClinicId)).UserId;
        }

        public async Task<HttpClient> NewHttpForAnotherUserAsync(string[] permissions)
        {
            string email;
            await using (var scope = _services.CreateSystemScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (_, email) = await CreateUserAsync(db, _hasher, TenantId, ClinicId);
            }

            var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(_services, email, permissions);
            var http = _createClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        /// <summary><paramref name="extra"/>: <c>&amp;</c> ile başlayan ek parametreler. Aralık tek gün (<see cref="Day"/>).</summary>
        public async Task<RegisterReadResult<T>> GetAsync<T>(string url, string? extra = null)
        {
            var response = await Http.GetAsync($"{url}?from={Day}&to={Day}{extra}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            return (await response.Content.ReadFromJsonAsync<RegisterReadResult<T>>())!;
        }

        /// <summary>CSV veri satırları (başlık hariç), hücrelere bölünmüş.</summary>
        public async Task<List<string[]>> GetCsvAsync(string url)
        {
            var response = await Http.GetAsync($"{url}/export?from={Day}&to={Day}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
            return Encoding.UTF8.GetString(bytes).TrimStart('﻿')
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(';')).ToList();
        }

        /// <summary>XLSX veri satırları (başlık satırları hariç).</summary>
        public async Task<List<IXLRow>> GetXlsxAsync(string url, int headerRows)
        {
            var response = await Http.GetAsync($"{url}/export-xlsx?from={Day}&to={Day}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            var wb = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            var ws = wb.Worksheet(1);
            var last = ws.LastRowUsed()!.RowNumber();
            return Enumerable.Range(headerRows + 1, Math.Max(0, last - headerRows)).Select(n => ws.Row(n)).ToList();
        }

        public async Task<Guid> SeedClientAsync(string name)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var client = new Client(TenantId, name, DateTime.UtcNow, $"90555{Interlocked.Increment(ref _phoneSeed):D7}");
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            return client.Id;
        }

        public async Task<Guid> SeedPetAsync(Guid clientId, string name)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var speciesId = await db.Species.OrderBy(s => s.DisplayOrder).Select(s => s.Id).FirstAsync();
            var pet = new Pet(TenantId, clientId, name, speciesId, breed: "Tekir", microchipNumber: "900111222333444");
            db.Pets.Add(pet);
            await db.SaveChangesAsync();
            return pet.Id;
        }

        public async Task<Guid> SeedClinicAsync()
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = new Clinic(TenantId, $"Other-{Guid.NewGuid():N}"[..14], "Bursa");
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            return clinic.Id;
        }

        public async Task<Guid> SeedProductAsync(
            string name,
            ProductKind kind,
            ControlledSubstanceClass controlled = ControlledSubstanceClass.None,
            string unit = "doz",
            decimal unitPrice = 10m,
            string? pharmacologicalForm = null,
            string? presentationForm = null,
            string? packaging = null)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new Product(
                TenantId, name, unit, unitPrice, "TRY", DateTime.UtcNow, kind: kind, controlledClass: controlled,
                pharmacologicalForm: pharmacologicalForm, presentationForm: presentationForm, packaging: packaging);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return product.Id;
        }

        /// <summary>
        /// Giriş hareketi: <paramref name="details"/> doluysa yeni parti açar (hareket partiye bağlanır), boşsa partisizdir (eski stok).
        /// Hareket ve parti doğrudan yazılır; stok satırı rapor için gerekmez.
        /// </summary>
        public async Task<Entry> SeedEntryAsync(
            Guid productId,
            decimal quantity,
            DateTime occurredAtUtc,
            StockBatchDetails? details,
            StockMovementType type = StockMovementType.In,
            Guid? clinicId = null)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clinic = clinicId ?? ClinicId;
            var movement = new StockMovement(TenantId, clinic, productId, type, quantity, occurredAtUtc, DateTime.UtcNow, createdByUserId: UserId);
            StockBatch? batch = null;
            if (details is not null)
            {
                var product = await db.Products.SingleAsync(p => p.Id == productId);
                batch = StockBatch.Create(TenantId, clinic, product, quantity, details, UserId, occurredAtUtc).Value!;
                movement.LinkBatch(batch);
            }

            db.StockMovements.Add(movement);
            await db.SaveChangesAsync();
            return new Entry(movement.Id, batch?.Id);
        }

        public async Task<Entry> SeedOutAsync(
            Guid productId,
            Guid? batchId,
            decimal quantity,
            DateTime occurredAtUtc,
            string reason,
            string? referenceType = null,
            Guid? referenceId = null)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var movement = new StockMovement(
                TenantId, ClinicId, productId, StockMovementType.Out, quantity, occurredAtUtc,
                DateTime.UtcNow,
                reason: reason, referenceType: referenceType, referenceId: referenceId, createdByUserId: UserId);
            if (batchId is { } id)
            {
                var batch = await db.StockBatches.SingleAsync(b => b.Id == id);
                batch.Decrease(quantity);
                movement.LinkBatch(batch);
            }

            db.StockMovements.Add(movement);
            await db.SaveChangesAsync();
            return new Entry(movement.Id, batchId);
        }

        /// <summary>Hareketin ters hareketi; partili hareketin partisine etkisi uygulanır. Ters hareketin kimliğini döner.</summary>
        public async Task<Guid> SeedReversalAsync(Guid movementId, DateTime occurredAtUtc, string reason)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var original = await db.StockMovements.SingleAsync(m => m.Id == movementId);
            var reversal = original.CreateReversal(reason, null, occurredAtUtc, UserId, null, DateTime.UtcNow);
            if (original.BatchId is { } id)
            {
                var batch = await db.StockBatches.SingleAsync(b => b.Id == id);
                if (original.ReversalIncreasesStock)
                    batch.Increase(original.Quantity);
                else
                    batch.Decrease(original.Quantity);
                reversal.LinkBatch(batch);
            }

            db.StockMovements.Add(reversal);
            await db.SaveChangesAsync();
            return reversal.Id;
        }

        /// <summary>Sayım düzeltmesi: partili ise partinin kalanı <paramref name="target"/> olur (miktar = |fark|); partisizde yalnız hareket yazılır.</summary>
        public async Task<Guid> SeedAdjustmentAsync(Guid productId, Guid? batchId, decimal target, DateTime occurredAtUtc)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            StockBatch? batch = null;
            var quantity = target;
            if (batchId is { } id)
            {
                batch = await db.StockBatches.SingleAsync(b => b.Id == id);
                quantity = Math.Abs(target - batch.RemainingQuantity);
                batch.SetRemaining(target);
            }

            var movement = new StockMovement(
                TenantId, ClinicId, productId, StockMovementType.Adjustment, quantity, occurredAtUtc,
                DateTime.UtcNow,
                reason: "Sayim", createdByUserId: UserId);
            if (batch is not null)
                movement.LinkBatch(batch);

            db.StockMovements.Add(movement);
            await db.SaveChangesAsync();
            return movement.Id;
        }

        public async Task<Guid> SeedAdministrationAsync(
            Guid petId,
            Guid productId,
            Guid? batchId,
            decimal quantity,
            DateTime administeredAtUtc,
            AdministrationFlags flags,
            Guid? createdBy = null,
            Guid? clinicId = null,
            Guid? responsibleVeterinarian = null)
        {
            await using var scope = _services.CreateSystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Guid? examinationId = null;
            if (responsibleVeterinarian is { } vetId)
            {
                var visit = new Visit(TenantId, clinicId ?? ClinicId, petId, null, vetId, UserId, administeredAtUtc);
                var examination = new Examination(
                    TenantId, clinicId ?? ClinicId, petId, null, administeredAtUtc, "Muayene", null, null, null, DateTime.UtcNow, visitId: visit.Id);
                db.Visits.Add(visit);
                db.Examinations.Add(examination);
                examinationId = examination.Id;
            }

            var late = flags.HasFlag(AdministrationFlags.Late);
            var administration = Administration.Create(
                    TenantId, clinicId ?? ClinicId, petId, examinationId, null, productId, quantity, administeredAtUtc, null,
                    late, late ? "Sonradan eklenen kayit" : null, createdBy ?? UserId, DateTime.UtcNow, null)
                .Value!;
            if (batchId is { } id)
                administration.LinkBatch(id);
            if (flags.HasFlag(AdministrationFlags.Voided))
            {
                administration.Void(UserId, "Yanlis kayit iptali", AdministrationVoidStockOutcome.WrittenOff, DateTime.UtcNow)
                    .IsSuccess.Should().BeTrue();
            }

            db.Administrations.Add(administration);
            await db.SaveChangesAsync();
            return administration.Id;
        }
    }
}
