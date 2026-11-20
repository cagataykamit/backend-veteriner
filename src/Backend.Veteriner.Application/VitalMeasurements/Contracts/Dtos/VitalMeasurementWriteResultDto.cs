namespace Backend.Veteriner.Application.VitalMeasurements.Contracts.Dtos;

/// <summary>PUT ve iptal yanıtı: istemci yeni sürümle formu yenilemeden devam eder.</summary>
public sealed record VitalMeasurementWriteResultDto(Guid Id, string RowVersion);
