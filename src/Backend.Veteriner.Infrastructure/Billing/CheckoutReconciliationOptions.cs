namespace Backend.Veteriner.Infrastructure.Billing;

/// <summary>Ödeme oturumlarının sağlayıcıyla periyodik uzlaştırması (<c>Billing:CheckoutReconciliation</c>).</summary>
public sealed class CheckoutReconciliationOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 300;

    /// <summary>Bu kadar dakika içinde açılmış oturumlar taranır; ödeme sayfası (iyzico token'ı) bu sürede çoktan kapanmıştır.</summary>
    public int LookbackMinutes { get; init; } = 180;

    public int BatchSize { get; init; } = 50;
}
