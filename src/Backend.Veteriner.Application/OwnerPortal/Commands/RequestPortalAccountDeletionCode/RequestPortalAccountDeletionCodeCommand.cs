using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.RequestPortalAccountDeletionCode;

/// <summary>
/// Oturumdaki hesabın e-postasına hesap silme onay kodu gönderir (ADR-041 K2). Bekleme süresindeyse yeni kod gitmez, yanıt
/// yine başarıdır (giriş kodu deseni). Süre, deneme ve bekleme kuralları <see cref="OwnerPortalCode"/>'dadır.
/// </summary>
public sealed record RequestPortalAccountDeletionCodeCommand(Guid OwnerAccountId)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "OwnerPortal.Account.DeletionCodeRequest";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

public sealed class RequestPortalAccountDeletionCodeCommandHandler : IRequestHandler<RequestPortalAccountDeletionCodeCommand, Result>
{
    private readonly OwnerPortalAccess _access;
    private readonly OwnerPortalCodeService _codes;
    private readonly TimeProvider _timeProvider;

    public RequestPortalAccountDeletionCodeCommandHandler(
        OwnerPortalAccess access,
        OwnerPortalCodeService codes,
        TimeProvider timeProvider)
    {
        _access = access;
        _codes = codes;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RequestPortalAccountDeletionCodeCommand request, CancellationToken ct)
    {
        var account = await _access.GetActiveAccountAsync(request.OwnerAccountId, ct);
        if (!account.IsSuccess)
            return Result.Failure(account.Error);

        await _codes.IssueAndSendAsync(account.Value!, OwnerPortalCodePurpose.AccountDeletion, _timeProvider.GetUtcNow().UtcDateTime, ct);
        return Result.Success();
    }
}
