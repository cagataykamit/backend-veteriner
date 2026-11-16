using System.Text.Json;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Reading;

namespace Backend.Veteriner.Application.Imports;

/// <summary>
/// Parti <c>WarningsJson</c> içeriği: dosya okunurken çıkan notlar, yanıttaki <c>warnings[]</c> biçimiyle. Okuma/yazma tek yerdedir.
/// Notlar kişisel veri taşımaz (sayı ve genel metin); satır verisi silinince de kalır.
/// </summary>
public static class ImportWarningsDocument
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ImportWarningDto> From(IEnumerable<ImportReadWarning> warnings)
        => warnings.Select(ToDto).ToList();

    public static string Serialize(IReadOnlyList<ImportWarningDto> warnings) => JsonSerializer.Serialize(warnings, Json);

    /// <summary>Not kaydı olmayan (alan eklenmeden önce yüklenmiş) partide null.</summary>
    public static IReadOnlyList<ImportWarningDto>? Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<List<ImportWarningDto>>(json, Json) ?? [];

    private static ImportWarningDto ToDto(ImportReadWarning w)
        => new(
            w.Code,
            w.Table is { } table ? ImportCanonicalNames.Key(table) : null,
            w.Message,
            w.Severity,
            w.Count,
            w.Unit,
            w.Title,
            w.Source,
            w.Parts?.Select(p => new ImportWarningPartDto(p.Label, p.Count, p.Severity)).ToList(),
            w.Outcome,
            w.Example,
            w.Target is { } t
                ? new ImportWarningTargetDto(t.Anchor, t.Table is { } tt ? ImportCanonicalNames.Key(tt) : null, t.Reason)
                : null);
}
