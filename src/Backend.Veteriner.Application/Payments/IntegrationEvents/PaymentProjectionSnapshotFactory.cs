using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Payments.IntegrationEvents;

/// <summary>
/// <see cref="Payment"/> aggregate'inden ve command handler'da doğrulanmış ilişkili kayıtlardan
/// finance + list projection snapshot üretir.
///
/// CQRS-14C: <paramref name="clientName"/> write path'te zorunludur (boş/null gelmemeli);
/// <paramref name="petName"/> payment'a pet bağlı değilse <c>null</c> olabilir.
/// Normalize değerler command-side normalizer'larıyla hizalıdır:
/// ClientNameNormalized = <see cref="Client.NormalizeFullNameForDuplicateCheck"/> (trim + invariant lower),
/// PetName/Notes normalize = trim + invariant lower.
///
/// CQRS-15D: <paramref name="clinicName"/> write path'te zorunludur; payment daima geçerli bir kliniğe
/// bağlıdır (domain ilişkisi). ClinicName display alanıdır, normalize edilmez.
/// </summary>
public static class PaymentProjectionSnapshotFactory
{
    /// <summary>
    /// Finance-only snapshot (CQRS-14C öncesi davranış). Backfill/parity finance hesapları için kullanılır;
    /// list enrichment alanları (ClientName/PetName/Notes) doldurulmaz. List projection için
    /// <see cref="Create(Payment, string, string?)"/> tercih edilmelidir.
    /// </summary>
    public static PaymentProjectionSnapshot Create(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentProjectionSnapshot(
            payment.Id,
            payment.TenantId,
            payment.ClinicId,
            payment.ClientId,
            payment.PetId,
            payment.AppointmentId,
            payment.ExaminationId,
            payment.Amount,
            payment.Currency,
            (int)payment.Method,
            payment.PaidAtUtc,
            PaymentIntegrationEventTypes.SchemaVersion);
    }

    public static PaymentProjectionSnapshot Create(
        Payment payment,
        string clientName,
        string clinicName,
        string? petName = null)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(clinicName);

        return new PaymentProjectionSnapshot(
            payment.Id,
            payment.TenantId,
            payment.ClinicId,
            payment.ClientId,
            payment.PetId,
            payment.AppointmentId,
            payment.ExaminationId,
            payment.Amount,
            payment.Currency,
            (int)payment.Method,
            payment.PaidAtUtc,
            PaymentIntegrationEventTypes.SchemaVersion,
            clientName.Trim(),
            Client.NormalizeFullNameForDuplicateCheck(clientName),
            NormalizeRaw(petName),
            NormalizeLower(petName),
            NormalizeRaw(payment.Notes),
            NormalizeLower(payment.Notes),
            clinicName.Trim());
    }

    private static string? NormalizeRaw(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeLower(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
