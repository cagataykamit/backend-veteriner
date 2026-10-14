using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using System.Text.Json;

namespace Backend.Veteriner.Application.Imports;

/// <summary>
/// Parti yanıtındaki <c>tables[]</c>: satır sayısı partiden, sütunlar dosya başlıklarından (hazırlık satırlarının ilki),
/// öneri kanonik şemadan, <c>mapped</c> kayıtlı eşlemeden gelir. Satırlar temizlenmişse (taslak silindi) sütun listesi boştur.
/// </summary>
public sealed class ImportTableInfoLoader
{
    private readonly IReadRepository<ImportBatchRow> _rows;

    public ImportTableInfoLoader(IReadRepository<ImportBatchRow> rows) => _rows = rows;

    public async Task<IReadOnlyList<ImportTableInfoDto>> LoadAsync(ImportBatch batch, CancellationToken ct)
    {
        var headers = new Dictionary<ImportTable, IReadOnlyList<string>>();
        if (batch.PayloadPurgedAtUtc is null)
        {
            foreach (var table in PresentTables(batch))
            {
                var first = await _rows.FirstOrDefaultAsync(new ImportFirstRowSpec(batch.Id, table), ct);
                headers[table] = HeadersOf(first?.SourceJson);
            }
        }

        return Build(batch, headers);
    }

    /// <summary>Yükleme anında satırlar bellekte olduğundan veritabanına dönmeden kurar.</summary>
    public static IReadOnlyList<ImportTableInfoDto> Build(
        ImportBatch batch,
        IReadOnlyDictionary<ImportTable, IReadOnlyList<string>> headers)
    {
        var mapping = ImportMappingDocument.Parse(batch.MappingJson);
        var result = new List<ImportTableInfoDto>();

        foreach (var table in ImportCanonicalNames.Tables)
        {
            var rowCount = RowCountOf(batch, table);
            if (rowCount == 0)
                continue;

            var tableHeaders = headers.TryGetValue(table, out var h) ? h : [];
            var suggestions = ImportCanonicalSchema.Suggest(table, tableHeaders);
            var hasMapping = mapping.Columns.ContainsKey(ImportCanonicalNames.Key(table));
            var mappedBySource = mapping.ColumnsOf(table)
                .GroupBy(kv => kv.Value, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

            result.Add(new ImportTableInfoDto(
                ImportCanonicalNames.Key(table),
                rowCount,
                suggestions
                    .Select(s => new ImportColumnDto(
                        s.Source,
                        s.Suggested,
                        s.Confidence,
                        hasMapping && mappedBySource.TryGetValue(s.Source, out var field) ? field : null))
                    .ToList()));
        }

        return result;
    }

    /// <summary>Hazırlık satırı kaynağındaki (<c>başlık → değer</c>) başlıklar, dosya sırasıyla.</summary>
    public static IReadOnlyList<string> HeadersOf(string? sourceJson)
    {
        if (string.IsNullOrWhiteSpace(sourceJson))
            return [];

        using var doc = JsonDocument.Parse(sourceJson);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            ? doc.RootElement.EnumerateObject().Select(p => p.Name).ToList()
            : [];
    }

    /// <summary>Partide satırı olan tablolar (kanonik sırayla).</summary>
    public static IReadOnlyList<ImportTable> PresentTables(ImportBatch batch)
        => ImportCanonicalNames.Tables.Where(t => RowCountOf(batch, t) > 0).ToList();

    public static int RowCountOf(ImportBatch batch, ImportTable table) => table switch
    {
        ImportTable.Customers => batch.CustomerCounts.Total,
        ImportTable.Pets => batch.PetCounts.Total,
        ImportTable.Vaccinations => batch.VaccinationCounts.Total,
        _ => 0,
    };
}
