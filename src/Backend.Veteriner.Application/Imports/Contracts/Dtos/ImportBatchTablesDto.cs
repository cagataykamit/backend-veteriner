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

/// <summary>
/// Dosya okunurken çıkan not (IMPORT_API_CONTRACT.md §7.2.1); partiye yazılır, parti ayrıntısıyla (<c>tables</c> ile) döner.
/// <see cref="Message"/> her notta vardır; diğer alanlar isteğe bağlıdır ve yoksa yazılmaz (ekran yalnız mesajı gösterir).
/// <see cref="Severity"/>: <c>decision | attention | info | skipped</c>.
/// </summary>
public sealed record ImportWarningDto(
    string Code,
    string? Table,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Severity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Count = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ImportWarningPartDto>? Parts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Example = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ImportWarningTargetDto? Target = null);

/// <summary>Notun alt kırılımı.</summary>
public sealed record ImportWarningPartDto(
    string Label,
    int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Severity = null);

/// <summary>Notun gideceği yer: <c>anchor = required | rows</c>; <c>rows</c> için <c>table</c> ve <c>reason</c>.</summary>
public sealed record ImportWarningTargetDto(
    string Anchor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Table = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);
