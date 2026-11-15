using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Public.TrialSignup;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.SignupEmailCode;

/// <summary>
/// E-postaya gelen kodu doğrular; başarıda kayıtta kullanılacak kısa ömürlü jeton döner (TRIAL-001). Yanlış deneme sonuç başarısız
/// olsa da kodda sayılır. Denetimde kod gizlidir.
/// </summary>
public sealed record VerifySignupEmailCodeCommand(string Email, string OneTimeCode)
    : IRequest<Result<SignupEmailVerifiedDto>>, IAuditableRequest
{
    public string AuditAction => "Public.OwnerSignup.EmailVerify";
    public string? AuditTarget => null;
}

public sealed class VerifySignupEmailCodeCommandHandler
    : IRequestHandler<VerifySignupEmailCodeCommand, Result<SignupEmailVerifiedDto>>
{
    private readonly SignupEmailVerificationService _verification;
    private readonly TimeProvider _timeProvider;

    public VerifySignupEmailCodeCommandHandler(SignupEmailVerificationService verification, TimeProvider timeProvider)
    {
        _verification = verification;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<SignupEmailVerifiedDto>> Handle(VerifySignupEmailCodeCommand request, CancellationToken ct)
        => await _verification.VerifyCodeAsync(
            SignupEmailVerification.NormalizeEmail(request.Email)!, request.OneTimeCode, _timeProvider.GetUtcNow().UtcDateTime, ct);
}
