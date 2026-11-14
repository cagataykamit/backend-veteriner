using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Public.PhoneVerification;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.SignupPhoneCode;

/// <summary>Deneme kaydı için cep telefonuna doğrulama kodu gönderir (TRIAL-001). Denetimde numara maskelidir.</summary>
public sealed record RequestSignupPhoneCodeCommand(string MobilePhone)
    : IRequest<Result<SignupPhoneCodeSentDto>>, IAuditableRequest
{
    public string AuditAction => "Public.OwnerSignup.PhoneCodeRequest";
    public string? AuditTarget => TurkishMobilePhone.Mask(MobilePhone);
}

public sealed class RequestSignupPhoneCodeCommandHandler
    : IRequestHandler<RequestSignupPhoneCodeCommand, Result<SignupPhoneCodeSentDto>>
{
    private readonly SignupPhoneVerificationService _verification;
    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;

    public RequestSignupPhoneCodeCommandHandler(
        SignupPhoneVerificationService verification,
        IClientContext client,
        TimeProvider timeProvider)
    {
        _verification = verification;
        _client = client;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<SignupPhoneCodeSentDto>> Handle(RequestSignupPhoneCodeCommand request, CancellationToken ct)
    {
        TurkishMobilePhone.TryNormalize(request.MobilePhone, out var phone);
        return await _verification.RequestCodeAsync(
            phone!, _client.IpAddress, _timeProvider.GetUtcNow().UtcDateTime, ct);
    }
}
