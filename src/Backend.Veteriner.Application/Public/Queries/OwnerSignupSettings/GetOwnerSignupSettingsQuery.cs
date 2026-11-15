using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Public.TrialSignup;
using Mediator;

namespace Backend.Veteriner.Application.Public.Queries.OwnerSignupSettings;

/// <summary>
/// Kayıt formunun sunucu ayarları (TRIAL-001): vergi numarası zorunluluğu ve Turnstile site anahtarı tek kaynaktan; panel, site ve
/// portal anahtarı koddan değil buradan okur.
/// </summary>
public sealed record GetOwnerSignupSettingsQuery : IRequest<OwnerSignupSettingsDto>;

public sealed class GetOwnerSignupSettingsQueryHandler : IRequestHandler<GetOwnerSignupSettingsQuery, OwnerSignupSettingsDto>
{
    private readonly TrialSignupIdentityService _identities;
    private readonly IHumanVerifier _humanVerifier;

    public GetOwnerSignupSettingsQueryHandler(TrialSignupIdentityService identities, IHumanVerifier humanVerifier)
    {
        _identities = identities;
        _humanVerifier = humanVerifier;
    }

    public ValueTask<OwnerSignupSettingsDto> Handle(GetOwnerSignupSettingsQuery request, CancellationToken ct)
        => ValueTask.FromResult(new OwnerSignupSettingsDto(_identities.TaxIdentityRequired, _humanVerifier.SiteKey));
}
