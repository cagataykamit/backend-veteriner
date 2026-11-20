namespace Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;

/// <param name="Source"><see cref="VitalSeriesSources"/> değeri.</param>
/// <param name="ExaminationId">Muayene kaynağında noktanın muayenesi (<c>SourceId</c>); vital ölçümünde kaydın muayene bağı.</param>
public sealed record VitalSeriesPointDto(
    string Source,
    Guid SourceId,
    Guid ClinicId,
    DateTime MeasuredAtUtc,
    decimal? TemperatureC,
    int? HeartRateBpm,
    int? RespiratoryRatePerMin,
    decimal? WeightKg,
    Guid? HospitalizationId,
    Guid? ExaminationId);

/// <param name="Points">Artan zaman sırası (grafik sırası).</param>
/// <param name="Truncated">Sınırdan fazla nokta vardı; en yeni noktalar döndü.</param>
/// <param name="OmittedSources">Yetki yok nedeniyle okunmayan kaynaklar.</param>
public sealed record VitalSeriesDto(
    IReadOnlyList<VitalSeriesPointDto> Points,
    bool Truncated,
    IReadOnlyList<string> OmittedSources);
