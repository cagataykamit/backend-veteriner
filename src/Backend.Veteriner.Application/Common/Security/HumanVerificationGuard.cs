using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Common.Security;

/// <summary>
/// Korunan herkese açık uçlarda insan doğrulamasının tek kapısı: sonucu sade hata koduna çevirir
/// (TRIAL_SIGNUP_ABUSE_API_CONTRACT.md §3). Yapılandırılmamışsa ya da sağlayıcıya ulaşılamazsa geçirmez.
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

    public HumanVerificationGuard(IHumanVerifier verifier, IClientContext client)
    {
        _verifier = verifier;
        _client = client;
    }

    public async Task<Result> EnsureHumanAsync(string? token, string action, CancellationToken ct)
    {
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
