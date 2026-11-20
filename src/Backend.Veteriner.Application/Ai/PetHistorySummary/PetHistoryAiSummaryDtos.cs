using System.Text.Json.Serialization;

namespace Backend.Veteriner.Application.Ai.PetHistorySummary;

/// <summary>
/// AI hasta geçmişi özeti (docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §2). Her zaman taslaktır (<see cref="IsDraft"/>; ADR-008
/// madde 3): kaydedilmez, kayıt oluşturmaz. Her madde dayandığı kayıtları <see cref="PetHistoryAiSourceDto"/> ile gösterir.
/// </summary>
/// <param name="Status"><c>Generated</c>: model özet üretti. <c>NoRecords</c>: özetlenecek kayıt yok, model çağrılmadı.</param>
/// <param name="Model">Kullanılan model; <c>NoRecords</c>'ta <c>null</c>.</param>
/// <param name="DroppedItemCount">Kaynağı doğrulanamadığı için atılan madde sayısı.</param>
public sealed record PetHistoryAiSummaryDto(
    Guid PetId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetHistoryAiSummaryStatus Status,
    bool IsDraft,
    DateTime GeneratedAtUtc,
    string? Model,
    PetHistoryAiCoverageDto Coverage,
    IReadOnlyList<PetHistoryAiSectionDto> Sections,
    int DroppedItemCount);

public enum PetHistoryAiSummaryStatus
{
    Generated = 1,
    NoRecords = 2,
}

/// <summary>Modele gönderilen kayıtların kapsamı.</summary>
/// <param name="FromUtc">Gönderilen en eski kaydın tarihi; kayıt yoksa <c>null</c>.</param>
/// <param name="ToUtc">Gönderilen en yeni kaydın tarihi; kayıt yoksa <c>null</c>.</param>
/// <param name="RecordCount">Gönderilen klinik kayıt sayısı (hayvan profili hariç).</param>
/// <param name="RecordCounts">Türe göre gönderilen kayıt sayısı (yalnız sıfırdan büyükler).</param>
/// <param name="OmittedTypes">Kullanıcının okuma izni olmadığı için hiç okunmayan türler.</param>
/// <param name="Truncated">Girdi sınırı nedeniyle en eski kayıtların bir kısmı gönderilmedi.</param>
public sealed record PetHistoryAiCoverageDto(
    DateTime? FromUtc,
    DateTime? ToUtc,
    int RecordCount,
    IReadOnlyDictionary<string, int> RecordCounts,
    IReadOnlyList<string> OmittedTypes,
    bool Truncated);

public sealed record PetHistoryAiSectionDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetHistoryAiSectionKind Kind,
    string Title,
    IReadOnlyList<PetHistoryAiItemDto> Items);

public sealed record PetHistoryAiItemDto(string Text, IReadOnlyList<PetHistoryAiSourceDto> Sources);

/// <summary>Maddenin dayandığı kayıt; istemci <see cref="Type"/> → rota eşlemesiyle kayda bağlanır.</summary>
/// <param name="Type"><c>Pet</c> (hayvan profili / uyarıları) ya da zaman çizelgesi türü: <c>Examination</c>,
/// <c>Treatment</c>, <c>Prescription</c>, <c>LabResult</c>, <c>Vaccination</c>, <c>Hospitalization</c>.</param>
/// <param name="OccurredAtUtc">Kaydın tarihi (hayvan profili için <c>null</c>).</param>
public sealed record PetHistoryAiSourceDto(string Type, Guid Id, DateTime? OccurredAtUtc);
