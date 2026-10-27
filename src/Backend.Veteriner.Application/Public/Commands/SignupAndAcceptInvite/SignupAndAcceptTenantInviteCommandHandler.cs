using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Invites;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;

public sealed class SignupAndAcceptTenantInviteCommandHandler
    : IRequestHandler<SignupAndAcceptTenantInviteCommand, Result<TenantInviteAcceptResultDto>>
{
    private readonly ITokenHashService _tokenHash;
    private readonly IRepository<TenantInvite> _invites;
    private readonly IReadRepository<User> _usersRead;
    private readonly IUserRepository _usersWrite;
    private readonly IPasswordHasher _hasher;
    private readonly TenantInviteAcceptanceService _acceptance;
    private readonly IReadRepository<OperationClaim> _operationClaims;
    private readonly IRepository<LegalAcceptance> _legalAcceptances;
    private readonly TimeProvider _timeProvider;

    public SignupAndAcceptTenantInviteCommandHandler(
        ITokenHashService tokenHash,
        IRepository<TenantInvite> invites,
        IReadRepository<User> usersRead,
        IUserRepository usersWrite,
        IPasswordHasher hasher,
        TenantInviteAcceptanceService acceptance,
        IReadRepository<OperationClaim> operationClaims,
        IRepository<LegalAcceptance> legalAcceptances,
        TimeProvider timeProvider)
    {
        _tokenHash = tokenHash;
        _invites = invites;
        _usersRead = usersRead;
        _usersWrite = usersWrite;
        _hasher = hasher;
        _acceptance = acceptance;
        _operationClaims = operationClaims;
        _legalAcceptances = legalAcceptances;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<TenantInviteAcceptResultDto>> Handle(
        SignupAndAcceptTenantInviteCommand request,
        CancellationToken ct)
    {
        string hash;
        try
        {
            hash = _tokenHash.ComputeSha256(request.RawToken.Trim());
        }
        catch
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.TokenInvalid",
                "Davet token geçersiz.");
        }

        var invite = await _invites.FirstOrDefaultAsync(new TenantInviteByTokenHashSpec(hash), ct);
        if (invite is null)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.NotFound",
                "Davet bulunamadı.");
        }

        if (invite.Status != TenantInviteStatus.Pending)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.NotPending",
                "Bu davet artık geçerli değil.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (utcNow >= invite.ExpiresAtUtc)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.Expired",
                "Davet süresi dolmuş.");
        }

        var existing = await _usersRead.FirstOrDefaultAsync(new UserByEmailNormalizedSpec(invite.Email), ct);
        if (existing is not null)
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                "Invites.RequiresLogin",
                "Bu e-posta ile hesap zaten var; giriş yapıp daveti kabul edin.");
        }

        // Sunucu da doğrular (ADR-029 madde 4); hesap oluşturulmadan önce.
        var inviteClaim = await _operationClaims.FirstOrDefaultAsync(new OperationClaimByIdSpec(invite.OperationClaimId), ct);
        var requiredLegalDocuments = LegalDocumentCatalog.RequiredFor(inviteClaim is null ? [] : [inviteClaim.Name]);
        var legalCheck = LegalDocumentCatalog.EnsureAcceptsCurrent(requiredLegalDocuments, request.AcceptedLegalDocuments);
        if (!legalCheck.IsSuccess)
            return Result<TenantInviteAcceptResultDto>.Failure(legalCheck.Error);

        var user = new User(invite.Email, _hasher.Hash(request.Password), utcNow);
        await _usersWrite.AddAsync(user, ct);

        var accepted = await _acceptance.AcceptAsync(invite, user, ct);
        if (!accepted.IsSuccess)
            return accepted;

        await _legalAcceptances.AddRangeAsync(
            LegalDocumentCatalog.CreateAcceptances(user.Id, invite.TenantId, requiredLegalDocuments, utcNow), ct);
        await _legalAcceptances.SaveChangesAsync(ct);

        return accepted;
    }
}
