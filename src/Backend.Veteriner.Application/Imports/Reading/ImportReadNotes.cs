using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>
/// Kaynak programdan bağımsız okuma notları (kod, tür, metin tek yerde). E-vet dönüştürmesinin notları <c>EVetNotes</c>'tadır.
/// </summary>
internal static class ImportReadNotes
{
    public const string EncodingSuspectCode = "Imports.EncodingSuspect";
    public const string TableEmptyCode = "Imports.TableEmpty";

    public static ImportReadWarning EncodingSuspect(ImportTable table)
        => new(EncodingSuspectCode, table,
            $"{ImportCanonicalNames.Display(table)} dosyasında okunamayan karakterler var; kodlama (UTF-8 / Windows-1254) yanlış olabilir.")
        {
            Severity = ImportNoteSeverity.Attention,
        };

    /// <param name="sheet">Excel sayfası mı (yoksa CSV dosyası).</param>
    public static ImportReadWarning TableEmpty(ImportTable table, bool sheet)
        => new(TableEmptyCode, table,
            $"{ImportCanonicalNames.Display(table)} {(sheet ? "sayfasında" : "dosyasında")} veri satırı yok; atlandı.")
        {
            Severity = ImportNoteSeverity.Skipped,
        };
}
