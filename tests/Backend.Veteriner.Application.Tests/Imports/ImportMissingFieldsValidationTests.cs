using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Products;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>
/// Eksik alanlar (IMPORT_API_CONTRACT.md bölüm 0, 4.3, 12.x; 2026-11-12): müşteri <c>Not</c>, ürün <c>KdvOrani</c> ve
/// <c>AlisFiyati</c>. Sentetik veri.
/// </summary>
public sealed class ImportMissingFieldsValidationTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    private static readonly ImportCatalogs Catalogs = new(Species: [], Breeds: [], Colors: [], Vaccines: []);

    private static readonly Dictionary<ImportTable, string[]> FieldsOf = new()
    {
        [ImportTable.Customers] = ["MusteriNo", "AdSoyad", "Telefon", "Not"],
        [ImportTable.Products] = ["Ad", "SatisFiyati", "KdvOrani", "AlisFiyati", "Barkod"],
    };

    private static IReadOnlyList<ImportSourceRow> Table(ImportTable table, params string?[][] rows)
        => rows.Select((cells, i) => new ImportSourceRow(
                i + 2,
                FieldsOf[table].Select((h, c) => (h, v: c < cells.Length ? cells[c] : null)).ToDictionary(x => x.h, x => x.v)))
            .ToList();

    private static ImportRowResult Run(ImportTable table, string?[] cells, ImportExistingData? existing = null)
    {
        var mapping = new ImportMappingDocument
        {
            Columns = new() { [ImportCanonicalNames.Key(table)] = FieldsOf[table].ToDictionary(f => f, f => f) },
        };
        var output = ImportValidationEngine.Run(new ImportValidationInput(
            new Dictionary<ImportTable, IReadOnlyList<ImportSourceRow>> { [table] = Table(table, cells) }, mapping, new ImportOptionsDocument(ImportOptionsDocument.Match), Catalogs,
            existing ?? ImportExistingData.Empty, Now));
        return output.Rows.Single(r => r.Table == table && r.RowNumber == 2);
    }

    private static string[] Codes(ImportRowResult r) => r.Issues.Select(i => i.Code).ToArray();

    // ================================================================ Müşteri notu

    [Fact]
    public void Customer_Note_Should_Be_Normalized_For_New_Customer()
    {
        var row = Run(ImportTable.Customers, ["M-1", "Deneme Müşteri", "05321234567", "  Meslek: Öğretmen\nAkşam arayın  "]);

        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var customer = (ImportNormalizedCustomer)row.Normalized;
        customer.Action.Should().Be(ImportRowAction.Create);
        customer.Notes.Should().Be("Meslek: Öğretmen\nAkşam arayın");
    }

    [Fact]
    public void Customer_Note_Over_Limit_Should_Be_Field_Too_Long_Error()
    {
        var row = Run(ImportTable.Customers, ["M-1", "Deneme Müşteri", null, new string('n', Client.MaxNotesLength + 1)]);

        row.Status.Should().Be(ImportRowValidationStatus.Error);
        row.Issues.Should().ContainSingle(i => i.Code == ImportRules.FieldTooLong.Code && i.Field == "Not");
    }

    // ================================================================ Ürün KDV ve alış fiyatı

    [Theory]
    [InlineData("20", 20)]
    [InlineData("%20", 20)]
    [InlineData("18,5", 18.5)]
    [InlineData("0", 0)]
    public void Product_Vat_Rate_Should_Be_Read(string raw, double expected)
    {
        var row = Run(ImportTable.Products, ["Deneme ürün", "100", raw]);

        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedProduct)row.Normalized).VatRate.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("yirmi")]
    [InlineData("120")]
    [InlineData("18,555")]
    [InlineData("-5")]
    public void Unreadable_Vat_Rate_Should_Warn_And_Import_Without_Rate(string raw)
    {
        var row = Run(ImportTable.Products, ["Deneme ürün", "100", raw]);

        row.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(row).Should().Contain(ImportRules.VatRateInvalid.Code);
        ((ImportNormalizedProduct)row.Normalized).VatRate.Should().BeNull();
        ((ImportNormalizedProduct)row.Normalized).Action.Should().Be(ImportRowAction.Create);
    }

    [Fact]
    public void Empty_Vat_Rate_Should_Mean_Clinic_Default()
    {
        var row = Run(ImportTable.Products, ["Deneme ürün", "100", null, null]);

        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        var product = (ImportNormalizedProduct)row.Normalized;
        (product.VatRate, product.PurchasePrice).Should().Be((null, null));
    }

    [Theory]
    [InlineData("₺1.250,50", 1250.50)]
    [InlineData("62,5", 62.5)]
    public void Product_Purchase_Price_Should_Be_Read(string raw, double expected)
        => ((ImportNormalizedProduct)Run(ImportTable.Products, ["Deneme ürün", "100", null, raw]).Normalized)
            .PurchasePrice.Should().Be((decimal)expected);

    [Theory]
    [InlineData("₺0,00")]
    [InlineData("0")]
    public void Zero_Purchase_Price_Should_Be_Empty(string raw)
    {
        var row = Run(ImportTable.Products, ["Deneme ürün", "100", null, raw]);

        row.Status.Should().Be(ImportRowValidationStatus.Valid);
        ((ImportNormalizedProduct)row.Normalized).PurchasePrice.Should().BeNull();
    }

    [Theory]
    [InlineData("on lira")]
    [InlineData("-10")]
    [InlineData("$15")]
    public void Invalid_Purchase_Price_Should_Warn_And_Import_Without_Price(string raw)
    {
        var row = Run(ImportTable.Products, ["Deneme ürün", "100", "20", raw]);

        row.Status.Should().Be(ImportRowValidationStatus.Warning);
        Codes(row).Should().Contain(ImportRules.PurchasePriceInvalid.Code);
        var product = (ImportNormalizedProduct)row.Normalized;
        (product.VatRate, product.PurchasePrice).Should().Be((20m, null));
    }

    [Fact]
    public void Matched_Product_Should_Match_Without_Overwriting()
    {
        var existingId = Guid.NewGuid();
        var existing = ImportExistingData.Empty with { Products = [new(existingId, "Eski ad", ProductKind.Other, "8690000000009")] };

        var row = Run(ImportTable.Products, ["Deneme ürün", "100", "10", "50", "8690000000009"], existing);

        row.Normalized.Action.Should().Be(ImportRowAction.Match);
        row.Normalized.TargetId.Should().Be(existingId);
    }

    // ================================================================ Şema

    [Theory]
    [InlineData(ImportTable.Customers, "Açıklama", "Not")]
    [InlineData(ImportTable.Customers, "Notlar", "Not")]
    [InlineData(ImportTable.Products, "Satış KDV", "KdvOrani")]
    [InlineData(ImportTable.Products, "KDV Oranı", "KdvOrani")]
    [InlineData(ImportTable.Products, "Alış Fiyatı", "AlisFiyati")]
    [InlineData(ImportTable.Products, "Maliyet", "AlisFiyati")]
    public void Schema_Should_Suggest_New_Fields_From_Common_Headers(ImportTable table, string header, string field)
        => ImportCanonicalSchema.Suggest(table, [header]).Single().Suggested.Should().Be(field);

    [Fact]
    public void Rule_Catalog_Should_Contain_New_Warning_Codes()
    {
        ImportRules.Find("Imports.VatRateInvalid")!.Severity.Should().Be(ImportIssueSeverity.Warning);
        ImportRules.Find("Imports.PurchasePriceInvalid")!.Severity.Should().Be(ImportIssueSeverity.Warning);
    }
}
