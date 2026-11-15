using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Security;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Public.TrialSignup;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.SignupEmailCode;

/// <summary>
/// Deneme kaydı için e-postaya doğrulama kodu gönderir (TRIAL-001). Önce insan doğrulaması (Turnstile), sonra tek deneme kuralı:
/// adresle daha önce deneme açılmışsa kod gönderilmez.
/// </summary>
public sealed record RequestSignupEmailCodeCommand(string Email, string? TurnstileToken)
    : IRequest<Result<SignupEmailCodeSentDto>>, IAuditableRequest
{
    public string AuditAction => "Public.OwnerSignup.EmailCodeRequest";
    public string? AuditTarget => null;
}

public sealed class RequestSignupEmailCodeCommandHandler
    : IRequestHandler<RequestSignupEmailCodeCommand, Result<SignupEmailCodeSentDto>>
{
    private readonly HumanVerificationGuard _human;
    private readonly TrialSignupIdentityService _identities;
    private readonly SignupEmailVerificationService _verification;
    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;

    public RequestSignupEmailCodeCommandHandler(
        HumanVerificationGuard human,
        TrialSignupIdentityService identities,
        SignupEmailVerificationService verification,
        IClientContext client,
        TimeProvider timeProvider)
    {
        _human = human;
        _identities = identities;
        _verification = verification;
        _client = client;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<SignupEmailCodeSentDto>> Handle(RequestSignupEmailCodeCommand request, CancellationToken ct)
    {
        var human = await _human.EnsureHumanAsync(request.TurnstileToken, HumanVerificationActions.SignupEmailCode, ct);
        if (!human.IsSuccess)
            return Result<SignupEmailCodeSentDto>.Failure(human.Error);

        var available = await _identities.EnsureEmailAvailableAsync(request.Email, ct);
        if (!available.IsSuccess)
            return Result<SignupEmailCodeSentDto>.Failure(available.Error);

        return await _verification.RequestCodeAsync(
            SignupEmailVerification.NormalizeEmail(request.Email)!, _client.IpAddress, _timeProvider.GetUtcNow().UtcDateTime, ct);
    }
}
