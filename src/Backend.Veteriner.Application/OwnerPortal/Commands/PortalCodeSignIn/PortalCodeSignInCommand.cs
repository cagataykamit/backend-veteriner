using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.PortalCodeSignIn;

/// <summary>
/// E-posta + tek kullanımlık kodla giriş (AK-2 a). Yanlış kod hem kodda hem hesapta sayılır (kilit kuralı
/// <see cref="OwnerAccount"/>); başarısız denemeler sonuç başarısız olsa da kaydedilir. Her hata aynı yanıttır.
/// </summary>
public sealed record PortalCodeSignInCommand(string Email, string OneTimeCode)
    : IRequest<Result<PortalAuthTokensDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.Auth.CodeSignIn";
    public string? AuditTarget => null;
}

public sealed class PortalCodeSignInCommandHandler : IRequestHandler<PortalCodeSignInCommand, Result<PortalAuthTokensDto>>
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly OwnerPortalCodeService _codes;
    private readonly OwnerPortalSessionIssuer _sessions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public PortalCodeSignInCommandHandler(
        IReadRepository<OwnerAccount> accounts,
        OwnerPortalCodeService codes,
        OwnerPortalSessionIssuer sessions,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _codes = codes;
        _sessions = sessions;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalAuthTokensDto>> Handle(PortalCodeSignInCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var email = OwnerAccount.NormalizeEmail(request.Email);
        var account = email is null ? null : await _accounts.FirstOrDefaultAsync(new OwnerAccountByEmailSpec(email), ct);
        if (account is null || !account.CanAttemptSignIn(utcNow))
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidCredentials);

        if (!await _codes.TryConsumeAsync(account, OwnerPortalCodePurpose.SignIn, request.OneTimeCode, utcNow, ct))
        {
            account.RecordFailedSignIn(utcNow);
            await _uow.SaveChangesAsync(ct);
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidCredentials);
        }

        account.RecordSuccessfulSignIn(utcNow);
        return Result<PortalAuthTokensDto>.Success(await _sessions.IssueAsync(account, utcNow, ct));
    }
}
