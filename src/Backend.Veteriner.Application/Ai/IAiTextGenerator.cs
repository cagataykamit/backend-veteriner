using System.Text.Json.Nodes;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// Sağlayıcıdan bağımsız AI metin üretimi (ADR-008 madde 7). Uygulama yalnız bu arayüzü bilir; sağlayıcı istemcisi
/// Infrastructure'dadır. Anahtar yoksa <see cref="IsConfigured"/> <c>false</c>'tur ve çağrı yapılmaz. Uygulama istem ve
/// yanıt metnini günlüğe yazmaz.
/// </summary>
public interface IAiTextGenerator
{
    bool IsConfigured { get; }

    /// <summary>Yanıtı verilen JSON şemasına uygun tek bir JSON belgesi olarak ister. Sağlayıcı hatası istisna değil sonuçtur.</summary>
    Task<AiGenerationResult> GenerateJsonAsync(AiGenerationRequest request, CancellationToken ct);
}

/// <param name="Schema">Yanıtın JSON şeması (tüm nesnelerde <c>additionalProperties: false</c>).</param>
public sealed record AiGenerationRequest(
    string Model,
    string SystemPrompt,
    string UserContent,
    JsonObject Schema,
    int MaxOutputTokens);

public enum AiGenerationStatus
{
    Completed = 1,
    Truncated = 2,
    Refused = 3,
    Failed = 4,
}

/// <summary>Başarısızlık sınıfı (yalnız <see cref="AiGenerationStatus.Failed"/>).</summary>
public enum AiFailureKind
{
    None = 0,

    /// <summary>Geçici: ağ, zaman aşımı, 5xx, aşırı yük, sağlayıcı hız sınırı.</summary>
    Unavailable = 1,

    /// <summary>Kalıcı: anahtar geçersiz, yetki, hesap/ödeme, istek reddi.</summary>
    Rejected = 2,
}

/// <param name="Text">Modelin metin çıktısı (JSON beklenir); başarısızlıkta <c>null</c>.</param>
/// <param name="InputTokens">Sağlayıcının bildirdiği gerçek giriş token'ı (önbellek okuma/yazma dahil).</param>
/// <param name="OutputTokens">Sağlayıcının bildirdiği gerçek çıkış token'ı (düşünme dahil).</param>
public sealed record AiGenerationResult(
    AiGenerationStatus Status,
    string? Text,
    int InputTokens,
    int OutputTokens,
    AiFailureKind FailureKind = AiFailureKind.None)
{
    public static AiGenerationResult Failed(AiFailureKind kind) => new(AiGenerationStatus.Failed, null, 0, 0, kind);
}
