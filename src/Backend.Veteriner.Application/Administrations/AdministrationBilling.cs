using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;

namespace Backend.Veteriner.Application.Administrations;

/// <summary>
/// Uygulama satırının ücret görünürlüğü (RECORD-001): kaynaktan doğan "ücret bekliyor" kalemi ve ona bağlı ücret kaydı.
/// Kural tek yerde: durum metinleri ve tutar burada hesaplanır; finans bilgisi olduğundan yalnız <c>Charges.Read</c> iznine açıktır
/// (izin yoksa <c>null</c> döner, satırda ücret bloğu görünmez).
/// </summary>
internal static class AdministrationBilling
{
    /// <summary>Ücretlendirilebilir kalem doğmadı (ürün fiyatı 0 veya özellikten önceki kayıt).</summary>
    public const string None = "None";
    public const string Pending = "Pending";
    public const string Charged = "Charged";
    /// <summary>Ücrete çevrildi, sonra ücret kaydı iptal edildi.</summary>
    public const string ChargeVoided = "ChargeVoided";
    public const string Waived = "Waived";
    /// <summary>Uygulama iptal edildi, bekleyen kalem düştü.</summary>
    public const string Cancelled = "Cancelled";
    /// <summary>Fiyatlı tedavi paketine dahil (EXAM-007): kendi kalemi yok, ücreti paket kalemindedir.</summary>
    public const string IncludedInPackage = "IncludedInPackage";

    public static async Task<IReadOnlyDictionary<Guid, AdministrationBillingDto>?> ReadAsync(
        ICurrentUserPermissionChecker permissions,
        IReadRepository<ChargeSuggestion> suggestions,
        IReadRepository<Charge> charges,
        Guid tenantId,
        IReadOnlyCollection<Administration> administrations,
        CancellationToken ct)
    {
        if (!permissions.HasPermission(PermissionCatalog.Charges.Read))
            return null;
        if (administrations.Count == 0)
            return new Dictionary<Guid, AdministrationBillingDto>();

        var administrationIds = administrations.Where(a => !a.IsIncludedInPackage).Select(a => a.Id).Distinct().ToArray();

        var rows = await suggestions.ListAsync(
            new ChargeSuggestionsBySourcesSpec(tenantId, ChargeSuggestion.AdministrationSource, administrationIds), ct);
        var chargeIds = rows.Where(s => s.ChargeId.HasValue).Select(s => s.ChargeId!.Value).Distinct().ToArray();
        var chargesById = chargeIds.Length == 0
            ? new Dictionary<Guid, Charge>()
            : (await charges.ListAsync(new ChargesByIdsSpec(tenantId, chargeIds), ct)).ToDictionary(c => c.Id);
        var bySource = rows.ToDictionary(s => s.SourceId);

        return administrations.DistinctBy(a => a.Id).ToDictionary(
            a => a.Id,
            a => a.IsIncludedInPackage
                ? new AdministrationBillingDto(IncludedInPackage)
                : bySource.TryGetValue(a.Id, out var s) ? ToDto(s, chargesById) : new AdministrationBillingDto(None));
    }

    private static AdministrationBillingDto ToDto(ChargeSuggestion s, IReadOnlyDictionary<Guid, Charge> chargesById)
    {
        var suggestedAmount = s.SuggestedUnitPrice is { } price ? decimal.Round(price * s.Quantity, 2) : (decimal?)null;
        return s.Status switch
        {
            ChargeSuggestionStatus.Pending => new AdministrationBillingDto(Pending, s.SuggestedUnitPrice, suggestedAmount),
            ChargeSuggestionStatus.Converted when s.ChargeId is { } chargeId && chargesById.TryGetValue(chargeId, out var charge) =>
                new AdministrationBillingDto(charge.IsVoided ? ChargeVoided : Charged, s.SuggestedUnitPrice, suggestedAmount, charge.Id, charge.Amount),
            ChargeSuggestionStatus.Converted => new AdministrationBillingDto(Charged, s.SuggestedUnitPrice, suggestedAmount, s.ChargeId),
            ChargeSuggestionStatus.Waived => new AdministrationBillingDto(Waived, s.SuggestedUnitPrice, suggestedAmount, WaiveReason: s.WaiveReason),
            _ => new AdministrationBillingDto(Cancelled, s.SuggestedUnitPrice, suggestedAmount),
        };
    }
}
