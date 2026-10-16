using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Diagnostics;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Invites;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalInvitePreview;

/// <summary>Anonim davet önizlemesi: klinik adı, maskeli e-posta, hesap var mı ve kabul edilecek belgeler.</summary>
public sealed record GetPortalInvitePreviewQuery(string Token) : IRequest<Result<PortalInvitePreviewDto>>;

public sealed class GetPortalInvitePreviewQueryHandler : IRequestHandler<GetPortalInvitePreviewQuery, Result<PortalInvitePreviewDto>>
{
    private readonly OwnerPortalInviteResolver _inviteResolver;
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IReadRepository<LegalAcceptance> _legalAcceptances;
    private readonly TimeProvider _timeProvider;

    public GetPortalInvitePreviewQueryHandler(
        OwnerPortalInviteResolver inviteResolver,
        IReadRepository<OwnerAccount> accounts,
        IReadRepository<LegalAcceptance> legalAcceptances,
        TimeProvider timeProvider)
    {
        _inviteResolver = inviteResolver;
        _accounts = accounts;
        _legalAcceptances = legalAcceptances;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PortalInvitePreviewDto>> Handle(GetPortalInvitePreviewQuery request, CancellationToken ct)
    {
        var resolved = await _inviteResolver.ResolveUsableAsync(request.Token, _timeProvider.GetUtcNow().UtcDateTime, ct);
        if (!resolved.IsSuccess)
            return Result<PortalInvitePreviewDto>.Failure(resolved.Error);
        var (invite, _, clinic) = resolved.Value!;

        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByEmailSpec(invite.Email), ct);
        var required = account is null
            ? LegalDocumentCatalog.RequiredForOwnerAccount
            : LegalDocumentCatalog.Pending(
                LegalDocumentCatalog.RequiredForOwnerAccount,
                await _legalAcceptances.ListAsync(new LegalAcceptancesByOwnerAccountSpec(account.Id), ct));

        return Result<PortalInvitePreviewDto>.Success(new PortalInvitePreviewDto(
            clinic.Name,
            LogMasking.MaskEmail(invite.Email),
            invite.ExpiresAtUtc,
            account is not null,
            LegalDocumentCatalog.ToDtos(required)));
    }
}
