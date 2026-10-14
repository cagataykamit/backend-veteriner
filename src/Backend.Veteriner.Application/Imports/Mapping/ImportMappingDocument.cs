using System.Text.Json;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>Kaydedilmiş değer eşlemesi. <see cref="TargetId"/> null: bu değerli satırlar bilinçli atlanır.</summary>
public sealed record ImportValueMapping(string Kind, string Source, Guid? TargetId);

/// <summary>
/// Parti <c>MappingJson</c> içeriği: tablo anahtarı → (kanonik alan → kaynak sütun adı) ve değer eşlemeleri.
/// Seçenekler ayrı sütundadır (<see cref="ImportOptionsDocument"/>). Okuma/yazma tek yerdedir.
/// </summary>
public sealed class ImportMappingDocument
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Dictionary<string, Dictionary<string, string>> Columns { get; init; } = new(StringComparer.Ordinal);
    public List<ImportValueMapping> ValueMappings { get; init; } = [];

    public static ImportMappingDocument Empty => new();

    public static ImportMappingDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;

        try
        {
            return JsonSerializer.Deserialize<ImportMappingDocument>(json, Json) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Tablo için kanonik alan → kaynak sütun eşlemesi (yoksa boş).</summary>
    public IReadOnlyDictionary<string, string> ColumnsOf(ImportTable table)
        => Columns.TryGetValue(ImportCanonicalNames.Key(table), out var map)
            ? map
            : new Dictionary<string, string>();
}

/// <summary>Parti seçenekleri (<c>OptionsJson</c>). <see cref="OnDuplicateClient"/> ADR-019 K4.</summary>
public sealed record ImportOptionsDocument(string OnDuplicateClient)
{
    public const string Match = "Match";
    public const string Skip = "Skip";
    public const string Error = "Error";

    public static readonly IReadOnlyList<string> AllowedDuplicateClientModes = [Match, Skip, Error];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ImportOptionsDocument Default => new(Match);

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}
