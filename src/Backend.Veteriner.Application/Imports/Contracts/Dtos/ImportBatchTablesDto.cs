using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Imports.Mapping;

namespace Backend.Veteriner.Application.Imports.Contracts.Dtos;

/// <summary>Algılanan tablo: satır sayısı ve dosyadaki sütunlar (başlık sırasıyla).</summary>
public sealed record ImportTableInfoDto(string Table, int RowCount, IReadOnlyList<ImportColumnDto> Columns);

/// <summary>
/// Dosya sütunu. <see cref="Suggested"/> otomatik önerilen kanonik alan, <see cref="Mapped"/> kayıtlı eşlemedeki
/// kanonik alan (eşleme kaydedilmemişse null).
/// </summary>
public sealed record ImportColumnDto(
    string Source,
    string? Suggested,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ImportSuggestionConfidence Confidence,
    string? Mapped);

/// <summary>Hata olmayan yükleme bulgusu; yalnız yükleme yanıtında döner.</summary>
public sealed record ImportWarningDto(string Code, string? Table, string Message);
