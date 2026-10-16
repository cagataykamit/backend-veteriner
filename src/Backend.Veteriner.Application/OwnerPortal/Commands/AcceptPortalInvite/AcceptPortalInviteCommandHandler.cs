using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Invites;
using Backend.Veteriner.Application.OwnerPortal.Sessions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalInvite;

public sealed class AcceptPortalInviteCommandHandler
    : IRequestHandler<AcceptPortalInviteCommand, Result<PortalInviteAcceptResultDto>>
{
    private readonly OwnerPortalInviteResolver _inviteResolver;
    private readonly IRepository<OwnerAccount> _accounts;
    private readonly IRepository<OwnerAccountClientLink> _links;
    private readonly IRepository<LegalAcceptance> _legalAcceptances;
    private readonly IPasswordHasher _hasher;
    private readonly OwnerPortalSessionIssuer _sessions;
    private readonly TimeProvider _timeProvider;

    public AcceptPortalInviteCommandHandler(
        OwnerPortalInviteResolver inviteResolver,
        IRepository<OwnerAccount> accounts,
        IRepository<OwnerAccountClientLink> links,
        IRepository<LegalAcceptance> legalAcceptances,
        IPasswordHasher hasher,
        OwnerPortalSessionIssuer sessions,
        TimeProvider timeProvider)
    {
        _inviteResolver = inviteResolver;
        _accounts = accounts;
        _links = links;
        _legalAcceptances = legalAcceptances;
        _hasher = hasher;
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PortalInviteAcceptResultDto>> Handle(AcceptPortalInviteCommand request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var resolved = await _inviteResolver.ResolveUsableAsync(request.Token, utcNow, ct);
        if (!resolved.IsSuccess)
            return Result<PortalInviteAcceptResultDto>.Failure(resolved.Error);
        var (invite, client, _) = resolved.Value!;

        if (await _links.FirstOrDefaultAsync(new ActiveOwnerAccountClientLinkByClientSpec(invite.TenantId, invite.ClientId), ct) is not null)
        {
            return Result<PortalInviteAcceptResultDto>.Failure(
                "OwnerPortal.AlreadyLinked",
                "Bu müşteri kaydı başka bir portal hesabına bağlı.");
        }

        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByEmailSpec(invite.Email), ct);
        var isNewAccount = account is null;
        if (account is { IsActive: false })
            return Result<PortalInviteAcceptResultDto>.Failure("Portal.Invites.NotFound", "Davet geçersiz veya süresi dolmuş.");

        var pendingDocuments = isNewAccount
            ? LegalDocumentCatalog.RequiredForOwnerAccount
            : LegalDocumentCatalog.Pending(
                LegalDocumentCatalog.RequiredForOwnerAccount,
                await _legalAcceptances.ListAsync(new LegalAcceptancesByOwnerAccountSpec(account!.Id), ct));

        // Sunucu da doğrular (ADR-029 madde 4); hesap oluşturulmadan önce.
        var legalCheck = LegalDocumentCatalog.EnsureAcceptsCurrent(pendingDocuments, request.AcceptedLegalDocuments);
        if (!legalCheck.IsSuccess)
            return Result<PortalInviteAcceptResultDto>.Failure(legalCheck.Error);

        var hasPassword = !string.IsNullOrEmpty(request.Password);
        if (hasPassword && account is { HasPassword: true })
        {
            return Result<PortalInviteAcceptResultDto>.Failure(
                "Portal.Invites.PasswordAlreadySet",
                "Bu e-postanın portal hesabında parola var; davet kabulünde parola gönderilmemeli.");
        }

        if (isNewAccount)
        {
            var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? client.FullName : request.DisplayName;
            account = OwnerAccount.Create(invite.Email, displayName, utcNow);
            if (hasPassword)
                account.SetPasswordHash(_hasher.Hash(request.Password!));
            await _accounts.AddAsync(account, ct);
        }
        else if (hasPassword)
        {
            account!.SetPasswordHash(_hasher.Hash(request.Password!));
        }

        if (pendingDocuments.Count > 0)
        {
            await _legalAcceptances.AddRangeAsync(
                LegalDocumentCatalog.CreateOwnerAccountAcceptances(account!.Id, pendingDocuments, utcNow), ct);
        }

        var accepted = invite.Accept(account!.Id, utcNow);
        if (!accepted.IsSuccess)
            return Result<PortalInviteAcceptResultDto>.Failure(accepted.Error);

        var link = OwnerAccountClientLink.Create(
            account.Id, invite.TenantId, invite.ClinicId, invite.ClientId, invite.CreatedByUserId, utcNow);
        await _links.AddAsync(link, ct);

        // Bağlantının sahibi e-postayı kanıtladı: oturum açılır, önceki başarısız deneme sayacı sıfırlanır.
        account.RecordSuccessfulSignIn(utcNow);
        var tokens = await _sessions.IssueAsync(account, utcNow, ct);

        return Result<PortalInviteAcceptResultDto>.Success(new PortalInviteAcceptResultDto(
            link.Id,
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc));
    }
}
