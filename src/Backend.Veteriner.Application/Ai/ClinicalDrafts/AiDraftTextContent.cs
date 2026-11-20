using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

/// <summary>
/// Tek metinli taslak içeriği (AI-003 hasta sahibine açıklama, AI-004 bakım talimatı): <c>{ "text": "…" }</c>. Model metnindeki
/// <see cref="AiTextRedactor.PatientMask"/> taslağa yazılmadan önce hayvan adıyla değiştirilir.
/// </summary>
public static class AiDraftTextContent
{
    public const string TextKey = "text";

    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { [TextKey] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray(TextKey),
        ["additionalProperties"] = false,
    };

    /// <summary>Model yanıtı → taslak içeriği; geçersiz JSON, boş ya da <paramref name="maxLength"/>'i aşan metin <c>null</c>.</summary>
    public static JsonObject? Parse(string? json, string? petName, int maxLength)
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

        if (root is not JsonObject obj || !TryText(obj[TextKey], out var text))
            return null;

        text = AiTextRedactor.RestorePatient(text, petName).Trim();
        return text.Length is > 0 && text.Length <= maxLength ? new JsonObject { [TextKey] = text } : null;
    }

    /// <summary>Kabul edilen (düzenlenmiş olabilir) içeriğin metni; şemaya uymuyorsa <c>AiDrafts.Validation</c>.</summary>
    public static Result<string> Read(JsonObject content, int maxLength)
    {
        if (content.Count != 1 || !TryText(content[TextKey], out var text) || string.IsNullOrWhiteSpace(text))
            return Result<string>.Failure(AiErrors.InvalidDraftContent("İçerik yalnız boş olmayan 'text' alanını taşımalıdır."));

        text = text.Trim();
        return text.Length <= maxLength
            ? Result<string>.Success(text)
            : Result<string>.Failure(AiErrors.InvalidDraftContent($"Metin en çok {maxLength} karakter olabilir."));
    }

    private static bool TryText(JsonNode? node, out string text)
    {
        text = string.Empty;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var s))
            return false;

        text = s;
        return true;
    }
}
