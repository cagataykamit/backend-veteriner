using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.PortalPasswordSignIn;

/// <summary>
/// E-posta + parolayla giriş (AK-2 b). Parolası olmayan, kilitli veya bulunamayan hesap yanlış parolayla aynı yanıtı alır;
/// yanıt süresi farkı olmasın diye o durumlarda da parola özeti doğrulanır.
/// </summary>
public sealed record PortalPasswordSignInCommand(string Email, string Password)
    : IRequest<Result<PortalAuthTokensDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.Auth.PasswordSignIn";
    public string? AuditTarget => null;
}

public sealed class PortalPasswordSignInCommandHandler : IRequestHandler<PortalPasswordSignInCommand, Result<PortalAuthTokensDto>>
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly OwnerPortalSessionIssuer _sessions;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public PortalPasswordSignInCommandHandler(
        IReadRepository<OwnerAccount> accounts,
        IPasswordHasher hasher,
        OwnerPortalSessionIssuer sessions,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _hasher = hasher;
        _sessions = sessions;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalAuthTokensDto>> Handle(PortalPasswordSignInCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var email = OwnerAccount.NormalizeEmail(request.Email);
        var account = email is null ? null : await _accounts.FirstOrDefaultAsync(new OwnerAccountByEmailSpec(email), ct);

        if (account is null || !account.HasPassword || !account.CanAttemptSignIn(utcNow))
        {
            _hasher.VerifyAgainstDummyHash(request.Password);
            if (account is not null && account.CanAttemptSignIn(utcNow))
            {
                // Parolası olmayan hesapta parola denemesi de başarısız giriştir.
                account.RecordFailedSignIn(utcNow);
                await _uow.SaveChangesAsync(ct);
            }

            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidCredentials);
        }

        if (!_hasher.Verify(request.Password, account.PasswordHash!))
        {
            account.RecordFailedSignIn(utcNow);
            await _uow.SaveChangesAsync(ct);
            return Result<PortalAuthTokensDto>.Failure(OwnerPortalErrors.InvalidCredentials);
        }

        account.RecordSuccessfulSignIn(utcNow);
        return Result<PortalAuthTokensDto>.Success(await _sessions.IssueAsync(account, utcNow, ct));
    }
}
