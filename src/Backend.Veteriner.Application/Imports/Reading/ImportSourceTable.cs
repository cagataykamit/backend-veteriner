using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Dosyadan okunan tek veri satırı; <see cref="Cells"/> başlık sırasıyla hizalıdır (boş hücre null).</summary>
public sealed record ImportSourceRow(int RowNumber, IReadOnlyList<string?> Cells);

/// <summary>Bir mantıksal tablonun başlıkları ve veri satırları (tamamen boş satırlar atılmış).</summary>
public sealed record ImportSourceTable(ImportTable Table, IReadOnlyList<string> Headers, IReadOnlyList<ImportSourceRow> Rows);

/// <summary>Hata olmayan okuma bulgusu (<c>Imports.EncodingSuspect</c>, <c>Imports.TableEmpty</c>).</summary>
public sealed record ImportReadWarning(string Code, ImportTable? Table, string Message);

public sealed record ImportReadResult(
    IReadOnlyList<ImportSourceTable> Tables,
    IReadOnlyList<ImportReadWarning> Warnings);
