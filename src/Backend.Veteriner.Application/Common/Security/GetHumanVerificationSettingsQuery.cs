using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.Common.Security;

/// <param name="TurnstileSiteKey">Cloudflare Turnstile site anahtarı; boşsa widget çizilmez (yerel geliştirme).</param>
/// <param name="PortalProtected">Portal giriş, parola sıfırlama ve online randevu kodu isteği insan doğrulaması istiyor mu.</param>
public sealed record HumanVerificationSettingsDto(string? TurnstileSiteKey, bool PortalProtected);

/// <summary>Portal ve online randevu sayfası için insan doğrulaması ayarları (site anahtarı koddan değil sunucudan).</summary>
public sealed record GetHumanVerificationSettingsQuery : IRequest<HumanVerificationSettingsDto>;

public sealed class GetHumanVerificationSettingsQueryHandler
    : IRequestHandler<GetHumanVerificationSettingsQuery, HumanVerificationSettingsDto>
{
    private readonly HumanVerificationGuard _guard;

    public GetHumanVerificationSettingsQueryHandler(HumanVerificationGuard guard) => _guard = guard;

    public ValueTask<HumanVerificationSettingsDto> Handle(GetHumanVerificationSettingsQuery request, CancellationToken ct)
        => ValueTask.FromResult(new HumanVerificationSettingsDto(
            _guard.SiteKey,
            _guard.IsRequired(HumanVerificationActions.PortalSignInCode)));
}
