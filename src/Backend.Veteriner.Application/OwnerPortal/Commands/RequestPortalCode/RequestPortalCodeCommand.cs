using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.RequestPortalCode;

/// <summary>
/// Giriş (<see cref="OwnerPortalCodePurpose.SignIn"/>) veya parola sıfırlama (<see cref="OwnerPortalCodePurpose.PasswordReset"/>)
/// kodu ister. Hesap var/yok, kilitli ya da bekleme süresinde olsun yanıt her zaman başarıdır (numaralandırma yok); kod yalnız
/// aktif ve kilitsiz hesaba gider.
/// </summary>
public sealed record RequestPortalCodeCommand(string Email, OwnerPortalCodePurpose Purpose)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => Purpose == OwnerPortalCodePurpose.PasswordReset
        ? "Portal.Auth.PasswordResetRequest"
        : "Portal.Auth.CodeRequest";

    public string? AuditTarget => null;
}

public sealed class RequestPortalCodeCommandHandler : IRequestHandler<RequestPortalCodeCommand, Result>
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly OwnerPortalCodeService _codes;
    private readonly TimeProvider _timeProvider;

    public RequestPortalCodeCommandHandler(
        IReadRepository<OwnerAccount> accounts,
        OwnerPortalCodeService codes,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _codes = codes;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(RequestPortalCodeCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var email = OwnerAccount.NormalizeEmail(request.Email);
        var account = email is null ? null : await _accounts.FirstOrDefaultAsync(new OwnerAccountByEmailSpec(email), ct);

        if (account is not null && account.CanAttemptSignIn(utcNow))
            await _codes.IssueAndSendAsync(account, request.Purpose, utcNow, ct);

        return Result.Success();
    }
}
