using System.Globalization;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.VitalMeasurements;

/// <summary>
/// Doğrulanmış vital değerler (HOSP-004): ateş, nabız, solunum, kilo. Birim ve aralıklar tek yerde burada; içe aktarma ateş ve kilo
/// sınırlarını da buradan okur. En az bir değer zorunludur; ondalıklı değerler aralık denetiminden önce yuvarlanır.
/// </summary>
public sealed class VitalSigns
{
    public const decimal MinTemperatureC = 25m;
    public const decimal MaxTemperatureC = 45m;
    public const int TemperatureDecimals = 1;
    public const int MinHeartRateBpm = 1;
    public const int MaxHeartRateBpm = 400;
    public const int MinRespiratoryRatePerMin = 1;
    public const int MaxRespiratoryRatePerMin = 200;
    public const decimal MinWeightKg = 0.01m;
    public const decimal MaxWeightKg = 9999.99m;
    public const int WeightDecimals = 2;

    public const string ValueRequiredCode = "VitalMeasurements.ValueRequired";
    public const string ValidationCode = "VitalMeasurements.Validation";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public decimal? TemperatureC { get; }
    public int? HeartRateBpm { get; }
    public int? RespiratoryRatePerMin { get; }
    public decimal? WeightKg { get; }

    private VitalSigns(decimal? temperatureC, int? heartRateBpm, int? respiratoryRatePerMin, decimal? weightKg)
    {
        TemperatureC = temperatureC;
        HeartRateBpm = heartRateBpm;
        RespiratoryRatePerMin = respiratoryRatePerMin;
        WeightKg = weightKg;
    }

    public static Result<VitalSigns> Create(decimal? temperatureC, int? heartRateBpm, int? respiratoryRatePerMin, decimal? weightKg)
    {
        var temperature = temperatureC is { } t ? Math.Round(t, TemperatureDecimals, MidpointRounding.AwayFromZero) : (decimal?)null;
        var weight = weightKg is { } w ? Math.Round(w, WeightDecimals, MidpointRounding.AwayFromZero) : (decimal?)null;

        if (temperature is null && heartRateBpm is null && respiratoryRatePerMin is null && weight is null)
            return Result<VitalSigns>.Failure(ValueRequiredCode, "En az bir vital değer (ateş, nabız, solunum, kilo) girin.");
        if (temperature is < MinTemperatureC or > MaxTemperatureC)
            return Invalid($"Vücut sıcaklığı {MinTemperatureC}–{MaxTemperatureC} °C arasında olmalıdır.");
        if (heartRateBpm is < MinHeartRateBpm or > MaxHeartRateBpm)
            return Invalid($"Nabız {MinHeartRateBpm}–{MaxHeartRateBpm} atım/dk arasında olmalıdır.");
        if (respiratoryRatePerMin is < MinRespiratoryRatePerMin or > MaxRespiratoryRatePerMin)
            return Invalid($"Solunum sayısı {MinRespiratoryRatePerMin}–{MaxRespiratoryRatePerMin} /dk arasında olmalıdır.");
        if (weight is < MinWeightKg or > MaxWeightKg)
            return Invalid($"Kilo {MinWeightKg}–{MaxWeightKg} kg arasında olmalıdır.");

        return Result<VitalSigns>.Success(new VitalSigns(temperature, heartRateBpm, respiratoryRatePerMin, weight));
    }

    private static Result<VitalSigns> Invalid(FormattableString message)
        => Result<VitalSigns>.Failure(ValidationCode, message.ToString(Turkish));
}
