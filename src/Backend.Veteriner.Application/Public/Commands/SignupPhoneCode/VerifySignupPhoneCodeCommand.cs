using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Public.PhoneVerification;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.SignupPhoneCode;

/// <summary>
/// Telefona gelen kodu doğrular; başarıda kayıtta kullanılacak kısa ömürlü jeton döner (TRIAL-001). Yanlış deneme sonuç başarısız
/// olsa da kodda sayılır. Denetimde numara maskeli, kod gizlidir.
/// </summary>
public sealed record VerifySignupPhoneCodeCommand(string MobilePhone, string OneTimeCode)
    : IRequest<Result<SignupPhoneVerifiedDto>>, IAuditableRequest
{
    public string AuditAction => "Public.OwnerSignup.PhoneVerify";
    public string? AuditTarget => TurkishMobilePhone.Mask(MobilePhone);
}

public sealed class VerifySignupPhoneCodeCommandHandler
    : IRequestHandler<VerifySignupPhoneCodeCommand, Result<SignupPhoneVerifiedDto>>
{
    private readonly SignupPhoneVerificationService _verification;
    private readonly TimeProvider _timeProvider;

    public VerifySignupPhoneCodeCommandHandler(SignupPhoneVerificationService verification, TimeProvider timeProvider)
    {
        _verification = verification;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<SignupPhoneVerifiedDto>> Handle(VerifySignupPhoneCodeCommand request, CancellationToken ct)
    {
        TurkishMobilePhone.TryNormalize(request.MobilePhone, out var phone);
        return await _verification.VerifyCodeAsync(phone!, request.OneTimeCode, _timeProvider.GetUtcNow().UtcDateTime, ct);
    }
}
