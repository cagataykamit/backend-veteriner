using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Activation;

/// <summary>
/// Deneme kohortunun ham satırları (TRIAL-008): dönemde deneme başlatan her kiracı için abonelik + aktivasyon işaretleri.
/// Hesap <see cref="TrialActivationMetricsCalculator"/>'da; okuyucu yalnız veri getirir.
/// </summary>
public interface ITrialActivationMetricsReader
{
    /// <summary>`fromUtc ≤ TrialStartsAtUtc &lt; toUtc` olan abonelikler; aktivasyon kaydı yoksa alanlar <c>null</c>.</summary>
    Task<IReadOnlyList<TrialCohortRow>> ListCohortAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed record TrialCohortRow(
    Guid TenantId,
    DateTime TrialStartsAtUtc,
    DateTime? PaidActivatedAtUtc,
    DateTime? ExampleClinicLoadedAtUtc,
    DateTime? FirstActionAtUtc,
    ActivationSignalKind? FirstActionKind);
