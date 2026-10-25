using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Deneme aktivasyonu için "anlamlı aksiyon" sayılan komutun işareti (TRIAL-008). Komut başarıyla bittiğinde
/// <c>ActivationSignalBehavior</c> kiracının ilk aksiyonunu kaydeder; eşleme tablosu
/// <c>docs/TRIAL_ACTIVATION_METRICS_API_CONTRACT.md</c> §2.1.
/// </summary>
public interface IActivationSignalRequest
{
    ActivationSignalKind ActivationSignal { get; }
}
