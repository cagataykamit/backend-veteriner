using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.DeletePortalAccount;

/// <summary>
/// Hasta sahibi portal hesabını kodla onaylayıp siler (ADR-041 seçenek C). Tek kayıtta: hesap anonimleşir, tüm oturum ve
/// kodlar silinir, tüm kiracılardaki aktif bağlar "hesap silindi" gerekçesiyle kapanır ve eski adrese onay e-postası
/// outbox'a yazılır. Klinik kayıtları, hukuki kabul satırları, davetler ve denetim kayıtları kalır.
/// </summary>
public sealed record DeletePortalAccountCommand(Guid OwnerAccountId, string OneTimeCode)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "OwnerPortal.Account.Delete";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

public sealed class DeletePortalAccountCommandHandler : IRequestHandler<DeletePortalAccountCommand, Result>
{
    private readonly IRepository<OwnerAccount> _accounts;
    private readonly IRepository<OwnerPortalCode> _codeRows;
    private readonly IRepository<OwnerPortalRefreshToken> _refreshTokens;
    private readonly IRepository<OwnerAccountClientLink> _links;
    private readonly OwnerPortalCodeService _codes;
    private readonly ITenantScope _tenantScope;
    private readonly IEmailSender _email;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public DeletePortalAccountCommandHandler(
        IRepository<OwnerAccount> accounts,
        IRepository<OwnerPortalCode> codeRows,
        IRepository<OwnerPortalRefreshToken> refreshTokens,
        IRepository<OwnerAccountClientLink> links,
        OwnerPortalCodeService codes,
        ITenantScope tenantScope,
        IEmailSender email,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _codeRows = codeRows;
        _refreshTokens = refreshTokens;
        _links = links;
        _codes = codes;
        _tenantScope = tenantScope;
        _email = email;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(DeletePortalAccountCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(request.OwnerAccountId), ct);
        if (account is not { IsActive: true })
            return Result.Failure(OwnerPortalErrors.AccountUnavailable);

        if (!await _codes.TryConsumeAsync(account, OwnerPortalCodePurpose.AccountDeletion, request.OneTimeCode, utcNow, ct))
        {
            // Yanlış deneme kodda sayılır; hak bitince kod geçersizleşir.
            await _uow.SaveChangesAsync(ct);
            return Result.Failure(OwnerPortalErrors.DeletionCodeInvalid);
        }

        var formerEmail = account.Email!;
        var deleted = account.Delete(utcNow);
        if (!deleted.IsSuccess)
            return deleted;

        await _codeRows.DeleteRangeAsync(new OwnerPortalCodesByAccountSpec(account.Id), ct);
        await _refreshTokens.DeleteRangeAsync(new OwnerPortalRefreshTokensByAccountSpec(account.Id), ct);

        var message = OwnerPortalEmails.AccountDeleted();
        await _email.SendAsync(formerEmail, message.Subject, message.Body, ct);

        // Bağlar birden çok kiracıya yayılır; kapatma ve kayıt kiracı filtresi dışında, yalnız bu hesabın bağlarıyla.
        using (_tenantScope.AsSystem())
        {
            foreach (var link in await _links.ListAsync(new ActiveOwnerAccountClientLinksForCloseByAccountSpec(account.Id), ct))
                link.CloseByOwnerDeletion(utcNow);

            await _uow.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
