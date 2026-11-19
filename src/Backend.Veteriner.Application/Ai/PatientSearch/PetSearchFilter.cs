using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Ai.PatientSearch;

/// <summary>
/// Doğal dille hasta aramasının yapılandırılmış filtresi (AI-006; sözleşme docs/AI_PATIENT_SEARCH_API_CONTRACT.md §2). Model
/// serbest sorgu yazmaz; cümleyi yalnız bu alanlara çevirir. Aynı filtre kullanıcı düzenleyince AI'siz arama ucuna gider.
/// Tüm alanlar isteğe bağlıdır; verilen alanlar VE ile, bir listedeki terimler VEYA ile birleşir. Kurallar
/// <see cref="PetSearchFilterRules"/>'tadır.
/// </summary>
/// <param name="Species">Tür kodları (ör. <c>CAT</c>, <c>DOG</c>); yalnız aktif türler.</param>
/// <param name="AgeMinYears">Tamamlanmış yaş alt sınırı (dahil); doğum tarihi olmayan hayvan eşleşmez.</param>
/// <param name="AgeMaxYears">Tamamlanmış yaş üst sınırı (dahil).</param>
/// <param name="From">Klinik kayıt tarih aralığının başı (İstanbul günü, dahil).</param>
/// <param name="To">Klinik kayıt tarih aralığının sonu (İstanbul günü, dahil).</param>
/// <param name="ClinicalTerms">Muayenenin geliş nedeni, anamnez, bulgular ve değerlendirmesinde geçen şikâyet/tanı terimleri.</param>
/// <param name="VaccineTerms">Aşı adında geçen terimler.</param>
/// <param name="VaccineState">Aşı kaydının durumu; terim olmadan da verilebilir (ör. "gecikmiş aşısı olanlar").</param>
/// <param name="MedicationTerms">Reçete kalemi / başlığı / içeriği ve tedavi başlığı / açıklamasında geçen ilaç terimleri.</param>
public sealed record PetSearchFilter(
    IReadOnlyList<string>? Species = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetGender? Sex = null,
    bool? Neutered = null,
    int? AgeMinYears = null,
    int? AgeMaxYears = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    IReadOnlyList<string>? ClinicalTerms = null,
    IReadOnlyList<string>? VaccineTerms = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PetSearchVaccineState? VaccineState = null,
    IReadOnlyList<string>? MedicationTerms = null)
{
    /// <summary>Muayene, aşı ya da ilaç ölçütü (ya da tarih aralığı) var mı; varsa aktif kliniğin kayıtlarında aranır.</summary>
    [JsonIgnore]
    public bool HasClinicalCriteria
        => HasExaminationCriterion || HasVaccineCriterion || HasMedicationCriterion;

    /// <summary>Terim ya da (başka klinik ölçüt yokken) tarih aralığı → muayene kaydı aranır ("son 3 ayda gelenler").</summary>
    [JsonIgnore]
    public bool HasExaminationCriterion
        => ClinicalTerms is { Count: > 0 }
           || ((From is not null || To is not null) && !HasVaccineCriterion && !HasMedicationCriterion);

    [JsonIgnore]
    public bool HasVaccineCriterion => VaccineTerms is { Count: > 0 } || VaccineState is not null;

    [JsonIgnore]
    public bool HasMedicationCriterion => MedicationTerms is { Count: > 0 };

    [JsonIgnore]
    public bool IsEmpty
        => !HasClinicalCriteria
           && Species is not { Count: > 0 }
           && Sex is null
           && Neutered is null
           && AgeMinYears is null
           && AgeMaxYears is null
           && Status is null;
}

/// <summary>Aşı ölçütünün durumu. Ad API'de string olarak döner; mevcut adlar değişmez.</summary>
public enum PetSearchVaccineState
{
    /// <summary>İptal edilmemiş her aşı kaydı (uygulanmış ya da planlı).</summary>
    Any = 1,

    /// <summary>Uygulanmış; tarih aralığı uygulama tarihine bakar.</summary>
    Applied = 2,

    /// <summary>Planlı (gecikmiş dahil); tarih aralığı vade tarihine bakar.</summary>
    Scheduled = 3,

    /// <summary>Planlı ve vadesi geçmiş (ADR-028, <c>VaccinationDueRule</c>); vefat eden hayvan dahil edilmez.</summary>
    Overdue = 4,
}
