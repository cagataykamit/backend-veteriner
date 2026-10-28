namespace Backend.Veteriner.Application.Common.Billing;

/// <summary>Sağlayıcıda ödeme iptal/iade denemesinin sonucu (BILL-005).</summary>
/// <param name="Retryable">Başarısızlık geçici olabilir (ağ, sağlayıcı hatası); değilse elle iade gerekir.</param>
public sealed record ProviderRefundResult(bool Succeeded, bool Retryable, string? Reason)
{
    public static ProviderRefundResult Success() => new(true, false, null);

    public static ProviderRefundResult Failed(string reason, bool retryable = true) => new(false, retryable, reason);

    public static ProviderRefundResult NotSupported(string reason) => new(false, false, reason);
}
