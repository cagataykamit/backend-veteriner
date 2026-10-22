namespace Backend.Veteriner.Application.Visits.Contracts.Dtos;

/// <summary>
/// Ziyaretin ücret durumu (hesaplanan). Ücret varsa açık tutara göre, yoksa ücretlendirilebilir işlemin varlığına göre belirlenir.
/// </summary>
public enum VisitChargeIndicator
{
    NoChargeableWork = 0,
    ChargeMissing = 1,
    PaymentPending = 2,
    PartiallyPaid = 3,
    Paid = 4
}

/// <summary>
/// Klinik geneli açık iş sayıları. Kullanıcının okuma izni olmayan kayıt türüne bağlı alanlar null'dır.
/// </summary>
public sealed record OpenItemsSummaryDto(
    int? UnchargedVisitCount,
    int? OutstandingVisitCount,
    decimal? OutstandingAmount,
    int? AwaitingLabCount,
    int IncompleteVisitCount,
    int? HbsNotEnteredPrescriptionCount = null,
    int? HbsNotEnteredVaccinationCount = null);

public static class OpenItemsDefaults
{
    /// <summary>Ücretsiz ziyaret sayımının geriye bakış penceresi (İstanbul günü); ücret sistemi öncesi kayıtlar sayıma girmez.</summary>
    public const int UnchargedLookbackDays = 30;

    /// <summary>"HBS'e girilmedi" sayımının geriye bakış penceresi (İstanbul günü); HBS yardımcısı öncesi kayıtlar sayıma girmez (ADR-036).</summary>
    public const int HbsLookbackDays = 30;
}
