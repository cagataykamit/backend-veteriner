using System.Text.Json.Nodes;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Ai.Drafts;

/// <summary>
/// Bir AI özelliğinin taslağını kabulde hedef kayda yazan genişleme noktası (AI-009; docs/AI_DRAFTS_API_CONTRACT.md §4).
/// Her taslak üreten özellik (AI-001/003/004) bir uygulayıcı kaydeder (<c>services.AddScoped&lt;IAiDraftApplier, ...&gt;()</c>);
/// uygulayıcısı olmayan özellik için taslak üretilmez.
/// <list type="bullet">
/// <item>Yazım <b>mevcut komutlarla</b> yapılır (<c>ISender.Send</c>); klinik kurallar tek yerde, komutların içinde kalır.</item>
/// <item>Kabul işlemiyle aynı transaction'da çalışır; başarısız <c>Result</c> ya da istisna her şeyi geri alır, taslak taslak kalır.</item>
/// <item>Taslağı kaydetmez, durumunu değiştirmez; bunu kabul komutu yapar.</item>
/// <item>Hata mesajları klinik metin içermez (denetim kaydına yazılır).</item>
/// </list>
/// </summary>
public interface IAiDraftApplier
{
    AiFeature Feature { get; }

    /// <summary>
    /// Bu özelliğin taslağını görmek, kabul etmek ve atmak için gereken izin; hedef kaydın yazma izni (ör. <c>Examinations.Update</c>).
    /// <c>Ai.Use</c>'a ek olarak denetlenir; yazan komutun kendi kuralları ayrıca çalışır.
    /// </summary>
    string RequiredPermission { get; }

    /// <summary>
    /// İçeriği doğrular ve hedef kayda yazar. İçerik şemaya uymuyorsa <see cref="AiErrors.InvalidDraftContent"/> döner.
    /// </summary>
    /// <returns>Yazılan/oluşan kaydın kimliği (taslakta <see cref="AiDraft.AppliedRecordId"/> olarak saklanır).</returns>
    Task<Result<Guid?>> ApplyAsync(AiDraftApplication application, CancellationToken ct);

    /// <summary>
    /// Kabul edilen içerik üretilenden farklı mı (<c>AcceptedWithEdits</c>). Varsayılan: JSON derin karşılaştırma. İçerikte
    /// metin dışı kabul seçenekleri taşıyan özellik (ör. muayene notunda "yerine yaz") yalnız metni karşılaştırır.
    /// </summary>
    bool IsEdited(JsonObject generated, JsonObject accepted) => !JsonNode.DeepEquals(generated, accepted);
}

/// <param name="DraftId">Hedef kayda "AI taslağından" izi olarak yazılabilir.</param>
/// <param name="Content">Kabul edilen içerik: düzenlenmişse düzenlenmiş hali, değilse üretilen hali.</param>
/// <param name="WasEdited">Kullanıcı içeriği değiştirdi mi.</param>
public sealed record AiDraftApplication(
    Guid DraftId,
    Guid TenantId,
    Guid ClinicId,
    string TargetType,
    Guid TargetId,
    JsonObject Content,
    bool WasEdited);

public static class AiDraftApplierExtensions
{
    /// <summary>Özelliğin uygulayıcısı; yoksa <c>null</c>.</summary>
    public static IAiDraftApplier? For(this IEnumerable<IAiDraftApplier> appliers, AiFeature feature)
        => appliers.FirstOrDefault(a => a.Feature == feature);
}
