namespace Backend.Veteriner.Domain.Examinations;

/// <summary>
/// <see cref="Examination.UpdateClinicalContent"/> için kısmi güncelleme girdisi.
/// Metin alanlarında <c>null</c> = mevcut değere dokunma, boş/boşluk = temizle.
/// Vital değerlerde <c>null</c> = dokunma; temizlemek için <see cref="ClearVitals"/> kullanılır.
/// <see cref="ExaminedAtUtc"/> ve <see cref="VisitReason"/> her zaman verilir.
/// </summary>
public sealed record ExaminationClinicalUpdate(
    DateTime ExaminedAtUtc,
    string VisitReason,
    string? Findings = null,
    string? Assessment = null,
    string? Notes = null,
    string? Anamnesis = null,
    string? Plan = null,
    decimal? WeightKg = null,
    decimal? TemperatureC = null,
    int? HeartRateBpm = null,
    int? RespiratoryRatePerMin = null,
    DateTime? VitalsMeasuredAtUtc = null,
    bool ClearVitals = false)
{
    public bool HasVitalInput =>
        WeightKg.HasValue
        || TemperatureC.HasValue
        || HeartRateBpm.HasValue
        || RespiratoryRatePerMin.HasValue
        || VitalsMeasuredAtUtc.HasValue;
}
