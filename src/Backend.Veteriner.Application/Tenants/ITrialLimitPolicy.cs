using Backend.Veteriner.Application.Reports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Kiracının deneme kısıtlarına (<see cref="TrialLimits"/>) tabi olup olmadığını söyler. Uygulama noktaları
/// (rapor dışa aktarımı, içe aktarma okuma) bu servisi çağırır; arka plan hatırlatma işleyicisi elinde abonelik
/// kaydı olduğu için <see cref="TrialLimitPolicy.IsTrial(Domain.Tenants.TenantSubscription)"/> ile aynı kuralı uygular.
/// </summary>
public interface ITrialLimitPolicy
{
    /// <summary>Kiracı deneme kısıtlarına tabi mi. Sonuç istek (scope) içinde önbelleklenir.</summary>
    Task<bool> IsTrialAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Dışa aktarma satır tavanı: deneme → <see cref="TrialLimits.ExportMaxRows"/>, ücretli → <see cref="ReportsSharedLimits.MaxExportRows"/>.</summary>
    Task<ExportRowLimit> GetExportRowLimitAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>
/// Dışa aktarma satır tavanı ve aşım hatası. Deneme kiracısında <c>Trial.ExportLimitExceeded</c>; ücretlide modülün
/// kendi <c>*.ExportTooManyRows</c> kodu ve mevcut mesajı. Okuyucuya <c>take</c> olarak da <see cref="MaxRows"/> verilir.
/// </summary>
public sealed record ExportRowLimit(int MaxRows, bool IsTrial)
{
    public static ExportRowLimit Paid { get; } = new(ReportsSharedLimits.MaxExportRows, IsTrial: false);

    public static ExportRowLimit Trial { get; } = new(TrialLimits.ExportMaxRows, IsTrial: true);

    public static ExportRowLimit For(bool isTrial) => isTrial ? Trial : Paid;

    /// <summary>Eşleşen satır sayısı tavanı aşıyorsa hata; <paramref name="paidErrorCode"/> yalnız ücretli kiracıda kullanılır.</summary>
    public Result Ensure(long totalRows, string paidErrorCode)
    {
        if (totalRows <= MaxRows)
            return Result.Success();

        return IsTrial
            ? Result.Failure(TrialLimits.ExportLimitExceeded())
            : Result.Failure(paidErrorCode, $"Dışa aktarma en fazla {MaxRows} satır destekler; filtreyi daraltın.");
    }
}
