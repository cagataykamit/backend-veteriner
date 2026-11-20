using System.Text.Json;

namespace Backend.Veteriner.Application.Ai.PetHistorySummary;

/// <summary>
/// Model yanıtını doğrular ve bölümlere çevirir. Yapılandırılmış çıktıya rağmen yanıt güvenilmez sayılır: bilinmeyen bölüm
/// atlanır, girdide olmayan ref'ler (uydurma kaynak) atılır, hiç geçerli kaynağı kalmayan madde atılır (sayılır). Aynı bölüm
/// iki kez gelirse birleştirilir; bölümler sabit sırada döner, metin ve madde sayısı sınırlanır.
/// </summary>
public static class PetHistoryAiOutputParser
{
    /// <returns>JSON geçersizse ya da <c>sections</c> dizisi yoksa <c>null</c>.</returns>
    public static ParsedSummary? Parse(string? json, IReadOnlyDictionary<string, PetHistoryAiSourceDto> sources)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sections", out var sectionsElement)
                || sectionsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new Dictionary<PetHistoryAiSectionKind, List<PetHistoryAiItemDto>>();
            var dropped = 0;
            foreach (var section in sectionsElement.EnumerateArray())
            {
                if (section.ValueKind != JsonValueKind.Object
                    || !section.TryGetProperty("kind", out var kindElement)
                    || kindElement.ValueKind != JsonValueKind.String
                    || !Enum.TryParse<PetHistoryAiSectionKind>(kindElement.GetString(), ignoreCase: false, out var kind)
                    || !Enum.IsDefined(kind)
                    || !section.TryGetProperty("items", out var itemsElement)
                    || itemsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var list = items.TryGetValue(kind, out var existing) ? existing : items[kind] = [];
                foreach (var item in itemsElement.EnumerateArray())
                {
                    var parsed = ParseItem(item, sources);
                    if (parsed is null)
                    {
                        dropped++;
                        continue;
                    }

                    if (list.Count < PetHistoryAiPrompt.MaxItemsPerSection)
                        list.Add(parsed);
                }
            }

            var sections = items
                .Where(p => p.Value.Count > 0)
                .OrderBy(p => p.Key)
                .Select(p => new PetHistoryAiSectionDto(p.Key, PetHistoryAiPrompt.Titles[p.Key], p.Value))
                .ToList();
            return new ParsedSummary(sections, dropped);
        }
    }

    private static PetHistoryAiItemDto? ParseItem(JsonElement item, IReadOnlyDictionary<string, PetHistoryAiSourceDto> sources)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("text", out var textElement)
            || textElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(textElement.GetString()))
        {
            return null;
        }

        var refs = item.TryGetProperty("refs", out var refsElement) && refsElement.ValueKind == JsonValueKind.Array
            ? refsElement.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.String)
                .Select(r => r.GetString()!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(r => sources.TryGetValue(r, out var source) ? source : null)
                .OfType<PetHistoryAiSourceDto>()
                .ToList()
            : [];
        if (refs.Count == 0)
            return null;

        var text = textElement.GetString()!.Trim();
        if (text.Length > PetHistoryAiPrompt.MaxItemTextLength)
            text = text[..PetHistoryAiPrompt.MaxItemTextLength] + "…";
        return new PetHistoryAiItemDto(text, refs);
    }
}

/// <param name="DroppedItemCount">Geçerli kaynağı olmadığı ya da biçimi bozuk olduğu için atılan madde sayısı.</param>
public sealed record ParsedSummary(IReadOnlyList<PetHistoryAiSectionDto> Sections, int DroppedItemCount);
