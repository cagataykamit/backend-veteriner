using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private const int MaxProductNameLength = 300;
    private const int MaxProductUnitLength = 50;
    private const int MaxProductSkuLength = 100;
    private const int MaxProductDescriptionLength = 4000;
    private const string DefaultProductUnit = "Adet";

    private static readonly Dictionary<string, ProductKind> ProductKindValues = new(StringComparer.Ordinal)
    {
        ["hizmet"] = ProductKind.Service,
        ["islem"] = ProductKind.Service,
        ["service"] = ProductKind.Service,
        ["ilac"] = ProductKind.Medicine,
        ["medicine"] = ProductKind.Medicine,
        ["drug"] = ProductKind.Medicine,
        ["asi"] = ProductKind.Vaccine,
        ["vaccine"] = ProductKind.Vaccine,
        ["diger"] = ProductKind.Other,
        ["urun"] = ProductKind.Other,
        ["mama"] = ProductKind.Other,
        ["malzeme"] = ProductKind.Other,
        ["other"] = ProductKind.Other,
    };

    private readonly Dictionary<string, int> _fileProductsByBarcode = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, ProductKind Kind), int> _fileProductsByName = new();
    private readonly Dictionary<string, ImportExistingProduct> _dbProductsByBarcode = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, ProductKind Kind), ImportExistingProduct> _dbProductsByName = new();
    private readonly Dictionary<string, List<Guid>> _categoriesByName = new(StringComparer.Ordinal);

    private ImportRowResult ProcessProduct(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();

        var name = ImportValueParsers.NormalizeText(reader.Raw("Ad"));
        if (name is null)
            issues.Add(ImportRules.RequiredMissing.At("Ad"));
        else if (name.Length > MaxProductNameLength)
            issues.Add(ImportRules.FieldTooLong.At("Ad", $"Ürün adı en fazla {MaxProductNameLength} karakter olabilir."));

        var kind = ProductKind.Other;
        if (reader.Raw("UrunTuru") is { } rawKind)
        {
            if (ProductKindValues.TryGetValue(ImportTextFolding.Fold(rawKind), out var known))
                kind = known;
            else
                issues.Add(ImportRules.ProductKindDefaulted.At("UrunTuru", value: rawKind));
        }

        Guid? categoryId = null;
        if (reader.Raw("Kategori") is { } rawCategory)
        {
            categoryId = UniqueByName(_categoriesByName, rawCategory);
            if (categoryId is null)
                issues.Add(ImportRules.CategoryUnmapped.At("Kategori", value: rawCategory));
        }

        var unit = ReadText(reader, "Birim", MaxProductUnitLength, issues) ?? DefaultProductUnit;

        var price = 0m;
        var rawPrice = reader.Raw("SatisFiyati");
        var (parsedPrice, priceValid) = ImportValueParsers.ParseAmount(rawPrice);
        if (!priceValid || parsedPrice is < 0)
            issues.Add(ImportRules.AmountInvalid.At("SatisFiyati", value: rawPrice));
        else if (parsedPrice is { } p)
            price = p;

        var barcode = ReadText(reader, "Barkod", Product.BarcodeMaxLength, issues);
        var sku = ReadText(reader, "StokKodu", MaxProductSkuLength, issues);
        var manufacturer = ReadText(reader, "Uretici", Product.ManufacturerMaxLength, issues);
        var description = ReadText(reader, "Aciklama", MaxProductDescriptionLength, issues);

        var normalized = new ImportNormalizedProduct(name, kind, categoryId, unit, price, barcode, sku, manufacturer, description);
        if (HasError(issues))
            return Finish(ImportTable.Products, reader.RowNumber, issues, normalized);

        // Dosya içi mükerrer: aynı barkod, yoksa aynı katlanmış ad + tür; ilk görülen kazanır.
        var nameKey = (ImportTextFolding.Fold(name), kind);
        var canonicalRow = barcode is not null && _fileProductsByBarcode.TryGetValue(barcode, out var byBarcode)
            ? byBarcode
            : _fileProductsByName.TryGetValue(nameKey, out var byName) ? byName : (int?)null;
        if (canonicalRow is { } first)
        {
            issues.Add(ImportRules.DuplicateInFile.At(null, $"Aynı ürün {first}. satırda da var; bu satır atlanacak."));
            return Finish(ImportTable.Products, reader.RowNumber, issues, normalized, ImportRowAction.Skip, mergedIntoRow: first);
        }

        if (barcode is not null)
            _fileProductsByBarcode[barcode] = reader.RowNumber;
        _fileProductsByName.TryAdd(nameKey, reader.RowNumber);

        // Kiracıda mevcut ürün (K4 ilkesi): barkod, yoksa ad + tür; mevcut alanlar (fiyat dahil) güncellenmez.
        var existing = barcode is not null && _dbProductsByBarcode.TryGetValue(barcode, out var dbByBarcode)
            ? dbByBarcode
            : _dbProductsByName.GetValueOrDefault(nameKey);
        if (existing is not null)
        {
            issues.Add(ImportRules.MatchedExisting.At());
            return Finish(ImportTable.Products, reader.RowNumber, issues, normalized, ImportRowAction.Match, existing.Id);
        }

        return Finish(ImportTable.Products, reader.RowNumber, issues, normalized, ImportRowAction.Create);
    }
}
