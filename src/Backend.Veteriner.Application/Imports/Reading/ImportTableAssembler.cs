using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Okuyucuların ham satırını (<see cref="ImportRawRow"/>) tavanlara ve başlık kurallarına göre tabloya çevirir.</summary>
internal static class ImportTableAssembler
{
    public const int MaxHeaderChars = 200;

    /// <summary>Başlık yoksa (sayfa/dosya tamamen boş) null döner.</summary>
    public static Result<ImportSourceTable?> Assemble(
        ImportTable table,
        IEnumerable<ImportRawRow> rawRows,
        ImportFileLimits limits)
    {
        ImportRawRow? header = null;
        var data = new List<ImportRawRow>();
        // Tamamen boş satırlar sayılmaz; yine de sınırsız boş satır taramasını önlemek için tarama tavanı vardır.
        var scanCeiling = (long)limits.MaxRowsPerTable * 2 + 1_000;
        long scanned = 0;

        foreach (var row in rawRows)
        {
            if (++scanned > scanCeiling)
                return TooManyRows(table, limits);

            if (row.IsBlank)
                continue;

            if (row.Cells.Any(c => c is not null && c.Length > limits.MaxCellChars))
            {
                return Result<ImportSourceTable?>.Failure(
                    "Imports.FileTooLarge",
                    $"{ImportCanonicalNames.Display(table)} tablosunda {row.RowNumber}. satırda hücre {limits.MaxCellChars} karakteri aşıyor.");
            }

            if (header is null)
            {
                header = row;
                continue;
            }

            data.Add(row);
            if (data.Count > limits.MaxRowsPerTable)
                return TooManyRows(table, limits);
        }

        if (header is null)
            return Result<ImportSourceTable?>.Success(null);

        var columnCount = Math.Max(
            LastNonBlank(header.Cells) + 1,
            data.Count == 0 ? 0 : data.Max(r => LastNonBlank(r.Cells) + 1));

        var keptColumns = new List<int>();
        var headers = new List<string>();
        for (var c = 0; c < columnCount; c++)
        {
            var title = c < header.Cells.Count ? header.Cells[c]?.Trim() : null;
            if (string.IsNullOrEmpty(title))
            {
                // Başlığı boş sütun: verisi yoksa yok sayılır, varsa adlandırılır.
                if (!data.Any(r => c < r.Cells.Count && r.Cells[c] is not null))
                    continue;
                title = $"Sütun {c + 1}";
            }

            if (title.Length > MaxHeaderChars)
                title = title[..MaxHeaderChars];

            headers.Add(UniqueHeader(title, headers));
            keptColumns.Add(c);
        }

        if (headers.Count > limits.MaxColumns)
        {
            return Result<ImportSourceTable?>.Failure(
                "Imports.UnsupportedFile",
                $"{ImportCanonicalNames.Display(table)} tablosunda en fazla {limits.MaxColumns} sütun olabilir.");
        }

        var rows = data
            .Select(r => new ImportSourceRow(
                r.RowNumber,
                keptColumns.Select(c => c < r.Cells.Count ? r.Cells[c] : null).ToArray()))
            .ToList();

        return Result<ImportSourceTable?>.Success(new ImportSourceTable(table, headers, rows));
    }

    private static int LastNonBlank(IReadOnlyList<string?> cells)
    {
        for (var i = cells.Count - 1; i >= 0; i--)
        {
            if (cells[i] is not null)
                return i;
        }

        return -1;
    }

    private static string UniqueHeader(string title, List<string> existing)
    {
        if (!existing.Contains(title, StringComparer.OrdinalIgnoreCase))
            return title;

        for (var n = 2; ; n++)
        {
            var candidate = $"{title} ({n})";
            if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                return candidate;
        }
    }

    /// <summary>Deneme kiracısında tavan <see cref="TrialLimits.ImportMaxRowsPerTable"/> ve kod <c>Trial.ImportLimitExceeded</c> (TRIAL-004).</summary>
    private static Result<ImportSourceTable?> TooManyRows(ImportTable table, ImportFileLimits limits)
        => limits.IsTrial
            ? Result<ImportSourceTable?>.Failure(TrialLimits.ImportLimitExceeded(ImportCanonicalNames.Display(table)))
            : Result<ImportSourceTable?>.Failure(
                "Imports.TooManyRows",
                $"{ImportCanonicalNames.Display(table)} tablosunda en fazla {limits.MaxRowsPerTable} veri satırı olabilir.");
}

/// <summary>Okuyucunun ürettiği ham satır; boş/yalnız boşluk hücreler null'a indirilmiştir.</summary>
internal sealed record ImportRawRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    public bool IsBlank => Cells.All(c => c is null);
}
