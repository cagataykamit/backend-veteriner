using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Ai.PatientSearch;

/// <summary>
/// Cümlenin anlaşılan filtresi (AI-006; sözleşme §3). Kaydedilmez; arayüz "şöyle anladım" diye gösterir, kullanıcı düzenleyip
/// <c>ai/pet-search/results</c>'a gönderir.
/// </summary>
/// <param name="Filter">Doğrulanmış filtre (<see cref="PetSearchFilterRules"/>).</param>
/// <param name="IsEmpty">Hiçbir ölçüt anlaşılamadı; arama yapılamaz.</param>
/// <param name="Unsupported">Modelin filtreye çeviremediğini söylediği kısımlar.</param>
/// <param name="Dropped">Geçersiz olduğu için atılan değerler.</param>
public sealed record PetSearchInterpretationDto(
    PetSearchFilter Filter,
    bool IsEmpty,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<string> Dropped,
    string Model,
    DateTime InterpretedAtUtc);

/// <summary>Yapılandırılmış aramanın sonucu (AI-006; sözleşme §4). AI çağrısı yapılmaz.</summary>
/// <param name="Filter">Uygulanan (doğrulanmış) filtre.</param>
/// <param name="Dropped">Geçersiz olduğu için uygulanmayan değerler.</param>
/// <param name="Truncated">Bir ölçütte eşleşen kayıt sınırı aşıldı; en eski eşleşmeler aranmadı.</param>
/// <param name="OmittedTypes">Okuma izni olmadığı için aranmayan kayıt türleri (ör. ilaç ölçütünde <c>Treatment</c>).</param>
public sealed record PetSearchResultDto(
    PetSearchFilter Filter,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<PetSearchItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool Truncated,
    IReadOnlyList<string> OmittedTypes);

/// <param name="AgeYears">Tamamlanmış yaş; doğum tarihi yoksa <c>null</c>.</param>
/// <param name="Matches">Klinik ölçütlere uyan kayıtlar, en yeni önce (tür başına en çok üç); yalnız hayvan ölçütü varsa boş.</param>
public sealed record PetSearchItemDto(
    Guid PetId,
    string Name,
    string SpeciesCode,
    string SpeciesName,
    string? Breed,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Sex,
    bool IsNeutered,
    int? AgeYears,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus Status,
    Guid ClientId,
    string ClientName,
    IReadOnlyList<PetSearchMatchDto> Matches);

/// <param name="Type">Kayıt türü (timeline adlarıyla aynı): <c>Examination</c>, <c>Vaccination</c>, <c>Prescription</c>, <c>Treatment</c>.</param>
/// <param name="Text">Kısa metin: geliş nedeni, aşı adı, reçete ya da tedavi başlığı.</param>
/// <param name="IsOverdue">Planlı aşının vadesi geçmiş (yalnız <c>Vaccination</c>).</param>
public sealed record PetSearchMatchDto(string Type, Guid Id, DateTime? OccurredAtUtc, string Text, bool IsOverdue);
