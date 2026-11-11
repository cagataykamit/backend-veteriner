using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Reading.EVet;

/// <summary>
/// E-vet Rapor &gt; Depo &gt; Ürün Listesi (CSV, <c>;</c>). Gruplu rapordur: ürün türü başlığı (yalnız ilk hücre dolu, ardından boş
/// satır), altında üretici/alt grup başlıkları (ardından boş satır yok) ve ürün satırları
/// (<c>;#;Ürün;Barkod-1;Barkod-2;Alış KDV;Alış Fiyatı;Satış KDV;Satış Fiyatı;Durum;[+]</c>). Pasif ürün (<c>[-]</c>) atlanır.
/// Sonuç kanonik ürün tablosudur (IMPORT_API_CONTRACT.md §12.3, §13).
/// </summary>
internal static class EVetProductList
{
    private static readonly string[] Headers = ["Ad", "UrunTuru", "Kategori", "SatisFiyati", "Barkod", "Aciklama"];

    /// <summary>E-vet tür başlığı → kanonik ürün türü (katlanmış). Listede olmayan tür <c>Diğer</c>dir.</summary>
    private static readonly Dictionary<string, string> KindByType = new(StringComparer.Ordinal)
    {
        ["asi"] = "Aşı",
        ["ilac"] = "İlaç",
        ["hizmet"] = "Hizmet",
        ["laboratuvar"] = "Hizmet",
    };

    /// <summary>Dosya E-vet ürün listesi değilse null (genel CSV okuyucusu devralır).</summary>
    public static Result<ImportSourceTable?>? TryRead(byte[] content, ImportFileLimits limits, List<ImportReadWarning> warnings)
    {
        var records = EVetReports.Records(content).ToList();
        var headerIndex = records.Take(10).ToList().FindIndex(IsHeader);
        if (headerIndex < 0)
            return null;

        var header = records[headerIndex].Cells.Select(ImportTextFolding.Fold).ToList();
        int Column(string name) => header.IndexOf(name);
        var name = Column("urun");
        var barcode = Column("barkod1");
        var barcode2 = Column("barkod2");
        var price = Column("satisfiyati");

        var rows = new List<ImportSourceRow>();
        string? type = null;
        string? group = null;
        var passive = 0;

        for (var i = headerIndex + 1; i < records.Count; i++)
        {
            var cells = records[i].Cells;
            if (records[i].IsBlank)
                continue;

            if (EVetReports.At(cells, 0) is { } title && cells.Skip(1).All(c => c is null))
            {
                // Tür başlığının ardından boş satır gelir; alt grup başlığının ardından gelmez.
                var isType = i + 1 < records.Count && records[i + 1].IsBlank;
                if (isType)
                {
                    type = title.Trim();
                    group = null;
                }
                else
                {
                    group = title.Trim();
                }

                continue;
            }

            if (!EVetCells.IsInteger(EVetReports.At(cells, 1)) || EVetReports.At(cells, name) is not { } productName)
                continue;

            if (cells.Any(c => c?.Trim() == "[-]"))
            {
                passive++;
                continue;
            }

            if (rows.Count >= limits.MaxRowsPerTable)
                return ImportTableAssembler.TooManyRows(ImportTable.Products, limits);

            var kind = type is null ? null : KindByType.GetValueOrDefault(ImportTextFolding.Fold(type), "Diğer");
            var description = EVetCells.Join(
                "; ",
                group is null ? null : $"Grup: {group}",
                EVetReports.At(cells, barcode2) is { } second ? $"Barkod 2: {second.Trim()}" : null);

            rows.Add(new ImportSourceRow(
                records[i].RowNumber,
                [productName.Trim(), kind, type, EVetCells.Clean(EVetReports.At(cells, price)), EVetCells.Clean(EVetReports.At(cells, barcode)), description]));
        }

        if (passive > 0)
        {
            warnings.Add(new ImportReadWarning(
                EVetBackupConverter.WarningCode,
                ImportTable.Products,
                $"E-vet ürün listesi: {passive} pasif ürün ([-]) aktarılmayacak."));
        }

        return Result<ImportSourceTable?>.Success(rows.Count == 0 ? null : new ImportSourceTable(ImportTable.Products, Headers, rows));
    }

    private static bool IsHeader(ImportRawRow record)
    {
        var folded = record.Cells.Select(ImportTextFolding.Fold).ToList();
        return folded.Contains("urun") && folded.Contains("barkod1") && folded.Contains("satisfiyati");
    }
}
