using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.ConfirmPortalPasswordReset;

/// <summary>
/// Kodla parola sıfırlama/belirleme. Başarıda parola yazılır, kilit temizlenir ve hesabın tüm portal oturumları kapanır.
/// Kod hatası giriş hatasıyla aynı yanıttır ve hesap sayacına işlenir.
/// </summary>
public sealed record ConfirmPortalPasswordResetCommand(string Email, string OneTimeCode, string NewPassword)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.Auth.PasswordResetConfirm";
    public string? AuditTarget => null;
}

public sealed class ConfirmPortalPasswordResetCommandHandler : IRequestHandler<ConfirmPortalPasswordResetCommand, Result>
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly OwnerPortalCodeService _codes;
    private readonly OwnerPortalSessionIssuer _sessions;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public ConfirmPortalPasswordResetCommandHandler(
        IReadRepository<OwnerAccount> accounts,
        OwnerPortalCodeService codes,
        OwnerPortalSessionIssuer sessions,
        IPasswordHasher hasher,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _codes = codes;
        _sessions = sessions;
        _hasher = hasher;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(ConfirmPortalPasswordResetCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var email = OwnerAccount.NormalizeEmail(request.Email);
        var account = email is null ? null : await _accounts.FirstOrDefaultAsync(new OwnerAccountByEmailSpec(email), ct);
        if (account is null || !account.CanAttemptSignIn(utcNow))
            return Result.Failure(OwnerPortalErrors.InvalidCredentials);

        if (!await _codes.TryConsumeAsync(account, OwnerPortalCodePurpose.PasswordReset, request.OneTimeCode, utcNow, ct))
        {
            account.RecordFailedSignIn(utcNow);
            await _uow.SaveChangesAsync(ct);
            return Result.Failure(OwnerPortalErrors.InvalidCredentials);
        }

        account.SetPasswordHash(_hasher.Hash(request.NewPassword));
        account.ClearLockout();
        await _sessions.RevokeAllAsync(account.Id, "password-reset", utcNow, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
