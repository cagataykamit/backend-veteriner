using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Common.Security;

/// <summary>
/// Korunan herkese açık uçlarda insan doğrulamasının tek kapısı: hangi eylemin korunduğuna karar verir ve sonucu sade hata koduna
/// çevirir (TRIAL_SIGNUP_ABUSE_API_CONTRACT.md §4). Yapılandırılmamışsa ya da sağlayıcıya ulaşılamazsa geçirmez.
/// </summary>
public sealed class HumanVerificationGuard
{
    public static readonly Error Failed = new(
        "HumanVerification.Failed",
        "Güvenlik doğrulaması tamamlanamadı. Sayfayı yenileyip yeniden deneyin.");

    public static readonly Error Unavailable = new(
        "HumanVerification.Unavailable",
        "Güvenlik doğrulaması şu an yapılamıyor. Lütfen biraz sonra yeniden deneyin.");

    private readonly IHumanVerifier _verifier;
    private readonly IClientContext _client;
    private readonly HumanVerificationOptions _options;

    public HumanVerificationGuard(IHumanVerifier verifier, IClientContext client, IOptions<HumanVerificationOptions> options)
    {
        _verifier = verifier;
        _client = client;
        _options = options.Value;
    }

    /// <summary>Eylem korunuyor mu: portal giriş/sıfırlama kodu ayara bağlı, diğerleri her zaman.</summary>
    public bool IsRequired(string action) => action switch
    {
        HumanVerificationActions.PortalSignInCode or HumanVerificationActions.PortalPasswordReset => _options.ProtectPortalSignIn,
        _ => true,
    };

    public string? SiteKey => _verifier.SiteKey;

    public async Task<Result> EnsureHumanAsync(string? token, string action, CancellationToken ct)
    {
        if (!IsRequired(action))
            return Result.Success();

        if (!_verifier.IsConfigured)
            return Result.Failure(Unavailable);

        return await _verifier.VerifyAsync(token, _client.IpAddress, action, ct) switch
        {
            HumanVerificationResult.Passed => Result.Success(),
            HumanVerificationResult.Failed => Result.Failure(Failed),
            _ => Result.Failure(Unavailable),
        };
    }
}
