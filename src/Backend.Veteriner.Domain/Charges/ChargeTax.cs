using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Charges;

/// <summary>Ücret satırının vergi bilgisi (ADR-036): satır türü ve KDV oranı (<c>null</c> = klinik varsayılanı).</summary>
public readonly record struct ChargeTax(ChargeLineKind LineKind, decimal? VatRate)
{
    /// <summary>Verilmezse: hizmet, klinik varsayılanı KDV.</summary>
    public static ChargeTax Default => new(ChargeLineKind.Service, null);

    public Result Validate()
    {
        if (!Enum.IsDefined(LineKind))
            return Result.Failure("Charges.Validation", "Geçersiz satır türü.");
        if (!TaxRatePercent.IsValidOptional(VatRate))
            return Result.Failure("Charges.Validation", $"KDV oranı geçersiz. {TaxRatePercent.InvalidMessage}");
        return Result.Success();
    }
}
