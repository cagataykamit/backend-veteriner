using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Visits;

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

/// <summary>Listesi açılabilen özet sayısı: <c>Uncharged</c> = <see cref="OpenItemsSummaryDto.UnchargedVisitCount"/>, <c>Incomplete</c> = <see cref="OpenItemsSummaryDto.IncompleteVisitCount"/>.</summary>
public enum OpenItemVisitKind
{
    Uncharged = 0,
    Incomplete = 1
}

/// <summary>Açık iş listesinde bir ziyaret; satırdan ücret eklemeye veya gelişi kapatmaya gidilir.</summary>
/// <param name="LatestExaminationId">Ziyaretin en son muayenesi; yoksa null.</param>
/// <param name="ChargeableWorkCount">Yalnız <c>Uncharged</c> listesinde: ziyaretteki ücretlendirilebilir işlem sayısı (görünür türler).</param>
public sealed record OpenItemVisitDto(
    Guid VisitId,
    DateTime ArrivedAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] VisitCareStatus CareStatus,
    Guid PetId,
    string PetName,
    string? SpeciesName,
    Guid ClientId,
    string ClientName,
    Guid? ResponsibleVeterinarianUserId,
    string? ResponsibleVeterinarianName,
    Guid? LatestExaminationId,
    int? ChargeableWorkCount);

/// <param name="TotalCount">Özetteki sayıyla aynı; <paramref name="Items"/> en çok <c>MaxItems</c> satırdır.</param>
public sealed record OpenItemVisitsDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OpenItemVisitKind Kind,
    int TotalCount,
    IReadOnlyList<OpenItemVisitDto> Items);

public static class OpenItemsDefaults
{
    /// <summary>Ücretsiz ziyaret sayımının geriye bakış penceresi (İstanbul günü); ücret sistemi öncesi kayıtlar sayıma girmez.</summary>
    public const int UnchargedLookbackDays = 30;

    /// <summary>"HBS'e girilmedi" sayımının geriye bakış penceresi (İstanbul günü); HBS yardımcısı öncesi kayıtlar sayıma girmez (ADR-036).</summary>
    public const int HbsLookbackDays = 30;
}
