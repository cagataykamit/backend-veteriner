using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

/// <summary>
/// Muayene notu taslağının içeriği (AI-001; sözleşme §7.1):
/// <c>{ "fields": { "visitReason": "…", …, "weightKg": 4.2 }, "replaceFields": [] }</c>. Notta karşılığı olmayan alan
/// <c>fields</c>'ta yer almaz. <c>replaceFields</c> kabulde kullanıcının seçimidir: listelenen metin alanı muayenedeki metnin
/// <b>yerine</b> yazılır, listelenmeyen alan mevcut metnin <b>sonuna</b> eklenir (varsayılan; formdaki metin seçmeden silinmez).
/// </summary>
public static class ExamNoteDraftContent
{
    public const string FieldsKey = "fields";
    public const string ReplaceFieldsKey = "replaceFields";

    public const string VisitReason = "visitReason";
    public const string Anamnesis = "anamnesis";
    public const string Findings = "findings";
    public const string Assessment = "assessment";
    public const string Plan = "plan";
    public const string Notes = "notes";
    public const string WeightKg = "weightKg";
    public const string TemperatureC = "temperatureC";
    public const string HeartRateBpm = "heartRateBpm";
    public const string RespiratoryRatePerMin = "respiratoryRatePerMin";

    /// <summary>Metin alanları, formdaki sırayla.</summary>
    public static readonly IReadOnlyList<string> TextFields = [VisitReason, Anamnesis, Findings, Assessment, Plan, Notes];

    private static readonly string[] DecimalFields = [WeightKg, TemperatureC];
    private static readonly string[] IntegerFields = [HeartRateBpm, RespiratoryRatePerMin];

    /// <summary>Yanıt şeması: alanların hepsi isteğe bağlı (notta olmayan alan yazılmaz).</summary>
    public static JsonObject Schema()
    {
        var properties = new JsonObject();
        foreach (var field in TextFields)
            properties[field] = new JsonObject { ["type"] = "string" };
        foreach (var field in DecimalFields)
            properties[field] = new JsonObject { ["type"] = "number" };
        foreach (var field in IntegerFields)
            properties[field] = new JsonObject { ["type"] = "integer" };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
    }

    /// <summary>
    /// Model yanıtı → taslak içeriği. Yanıt güvenilmez sayılır: bilinmeyen alan ve türü yanlış değer atılır, boş metin alınmaz,
    /// <see cref="AiTextRedactor.PatientMask"/> hayvan adıyla değiştirilir. Geçerli hiçbir alan kalmazsa <c>null</c>.
    /// </summary>
    public static JsonObject? Parse(string? json, string? petName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject obj)
            return null;

        var fields = new JsonObject();
        foreach (var field in TextFields)
        {
            if (Str(obj[field]) is { } text && !string.IsNullOrWhiteSpace(text))
                fields[field] = AiTextRedactor.RestorePatient(text, petName).Trim();
        }

        foreach (var field in DecimalFields)
        {
            if (obj[field] is JsonValue value && value.TryGetValue<decimal>(out var number))
                fields[field] = number;
        }

        foreach (var field in IntegerFields)
        {
            if (obj[field] is JsonValue value && value.TryGetValue<int>(out var number))
                fields[field] = number;
        }

        return fields.Count == 0 ? null : new JsonObject { [FieldsKey] = fields, [ReplaceFieldsKey] = new JsonArray() };
    }

    /// <summary>Kabul edilen içerik; şemaya uymuyorsa <c>AiDrafts.Validation</c>. Boş metin ya da <c>null</c> alan aktarılmaz.</summary>
    public static Result<ExamNoteFields> Read(JsonObject content)
    {
        if (content[FieldsKey] is not JsonObject fields)
            return Invalid("İçerikte 'fields' nesnesi olmalıdır.");
        if (content.Any(p => p.Key is not (FieldsKey or ReplaceFieldsKey)))
            return Invalid("İçerikte yalnız 'fields' ve 'replaceFields' olabilir.");

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        decimal? weightKg = null, temperatureC = null;
        int? heartRateBpm = null, respiratoryRatePerMin = null;
        foreach (var (key, node) in fields)
        {
            if (TextFields.Contains(key))
            {
                if (node is not null && Str(node) is null)
                    return Invalid($"'{key}' metin olmalıdır.");
                if (Str(node) is { } value && !string.IsNullOrWhiteSpace(value))
                    texts[key] = value.Trim();
            }
            else if (DecimalFields.Contains(key) || IntegerFields.Contains(key))
            {
                if (node is null)
                    continue;

                var isInteger = IntegerFields.Contains(key);
                if (node is not JsonValue number
                    || (isInteger ? !number.TryGetValue<int>(out _) : !number.TryGetValue<decimal>(out _)))
                {
                    return Invalid(isInteger ? $"'{key}' tam sayı olmalıdır." : $"'{key}' sayı olmalıdır.");
                }

                switch (key)
                {
                    case WeightKg: weightKg = number.GetValue<decimal>(); break;
                    case TemperatureC: temperatureC = number.GetValue<decimal>(); break;
                    case HeartRateBpm: heartRateBpm = number.GetValue<int>(); break;
                    default: respiratoryRatePerMin = number.GetValue<int>(); break;
                }
            }
            else
            {
                return Invalid($"Bilinmeyen alan: '{key}'.");
            }
        }

        var replace = new HashSet<string>(StringComparer.Ordinal);
        if (content[ReplaceFieldsKey] is { } replaceNode)
        {
            if (replaceNode is not JsonArray array)
                return Invalid("'replaceFields' dizi olmalıdır.");
            foreach (var item in array)
            {
                if (Str(item) is not { } name || !TextFields.Contains(name))
                    return Invalid("'replaceFields' yalnız metin alanı adları taşıyabilir.");
                replace.Add(name);
            }
        }

        if (texts.Count == 0 && weightKg is null && temperatureC is null && heartRateBpm is null && respiratoryRatePerMin is null)
            return Invalid("Aktarılacak alan yok.");

        return Result<ExamNoteFields>.Success(
            new ExamNoteFields(texts, weightKg, temperatureC, heartRateBpm, respiratoryRatePerMin, replace));
    }

    /// <summary>Düzenlendi mi: yalnız alan değerleri karşılaştırılır; "yerine yaz" seçimi düzenleme sayılmaz.</summary>
    public static bool IsEdited(JsonObject generated, JsonObject accepted)
        => !JsonNode.DeepEquals(generated[FieldsKey], accepted[FieldsKey]);

    private static Result<ExamNoteFields> Invalid(string message) => Result<ExamNoteFields>.Failure(AiErrors.InvalidDraftContent(message));

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <param name="Texts">Aktarılacak metin alanları (anahtar <see cref="ExamNoteDraftContent.TextFields"/>; boş olanlar yok).</param>
/// <param name="Replace">Mevcut metnin yerine yazılacak metin alanları; diğerleri sona eklenir.</param>
public sealed record ExamNoteFields(
    IReadOnlyDictionary<string, string> Texts,
    decimal? WeightKg,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRatePerMin,
    IReadOnlySet<string> Replace);
