using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.CreateClientPortalInvite;

public sealed class CreateClientPortalInviteCommandHandler
    : IRequestHandler<CreateClientPortalInviteCommand, Result<ClientPortalInviteDto>>
{
    private readonly OwnerPortalStaffAccess _staffAccess;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<OwnerAccountClientLink> _links;
    private readonly IRepository<OwnerPortalInvite> _invites;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly IEmailSender _email;
    private readonly OwnerPortalOptions _portalOptions;
    private readonly AppOptions _appOptions;
    private readonly TimeProvider _timeProvider;

    public CreateClientPortalInviteCommandHandler(
        OwnerPortalStaffAccess staffAccess,
        IClinicContext clinicContext,
        IReadRepository<Clinic> clinics,
        IReadRepository<OwnerAccountClientLink> links,
        IRepository<OwnerPortalInvite> invites,
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        IEmailSender email,
        IOptions<OwnerPortalOptions> portalOptions,
        IOptions<AppOptions> appOptions,
        TimeProvider timeProvider)
    {
        _staffAccess = staffAccess;
        _clinicContext = clinicContext;
        _clinics = clinics;
        _links = links;
        _invites = invites;
        _secrets = secrets;
        _hash = hash;
        _email = email;
        _portalOptions = portalOptions.Value;
        _appOptions = appOptions.Value;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClientPortalInviteDto>> Handle(CreateClientPortalInviteCommand request, CancellationToken ct)
    {
        var scopeResult = await _staffAccess.ResolveClientAsync(request.ClientId, ct);
        if (!scopeResult.IsSuccess)
            return Result<ClientPortalInviteDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var eligibility = OwnerPortalInvitePolicy.CanInvite(scope.Client);
        if (!eligibility.IsSuccess)
            return Result<ClientPortalInviteDto>.Failure(eligibility.Error);

        if (await _links.FirstOrDefaultAsync(new ActiveOwnerAccountClientLinkByClientSpec(scope.TenantId, scope.Client.Id), ct) is not null)
        {
            return Result<ClientPortalInviteDto>.Failure(
                "OwnerPortal.AlreadyLinked",
                "Müşterinin portal erişimi zaten açık.");
        }

        if (request.ClinicId is { } bodyClinicId && _clinicContext.ClinicId is { } contextClinicId && bodyClinicId != contextClinicId)
        {
            return Result<ClientPortalInviteDto>.Failure(
                "OwnerPortal.ClinicContextMismatch",
                "İstek clinicId değeri aktif klinik bağlamı ile uyuşmuyor.");
        }

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
        {
            return Result<ClientPortalInviteDto>.Failure(
                "OwnerPortal.ClinicRequired",
                "Daveti gönderen klinik belirtilmelidir (aktif klinik veya clinicId).");
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(scope.TenantId, clinicId), ct);
        if (clinic is not { IsActive: true })
            return Result<ClientPortalInviteDto>.Failure("OwnerPortal.ClinicInvalid", "Klinik bulunamadı veya pasif.");

        var clinicAccess = await _staffAccess.EnsureClinicAssignedAsync(scope.UserId, clinic.Id, ct);
        if (!clinicAccess.IsSuccess)
            return Result<ClientPortalInviteDto>.Failure(clinicAccess.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        // Yeni davet bekleyen eskisinin yerine geçer (tek geçerli bağlantı).
        foreach (var pending in await _invites.ListAsync(new PendingOwnerPortalInvitesByClientSpec(scope.TenantId, scope.Client.Id), ct))
            pending.Revoke(scope.UserId, utcNow);

        var rawToken = _secrets.CreateToken();
        var invite = OwnerPortalInvite.CreatePending(
            scope.TenantId,
            clinic.Id,
            scope.Client.Id,
            scope.Client.Email!,
            _hash.ComputeSha256(rawToken),
            scope.UserId,
            utcNow.AddDays(_portalOptions.InviteExpiryDays),
            utcNow);

        var message = OwnerPortalEmails.Invite(
            clinic.Name,
            OwnerPortalEmails.InviteLink(_portalOptions, _appOptions, rawToken),
            _portalOptions.InviteExpiryDays);
        await _email.SendAsync(invite.Email, message.Subject, message.Body, ct);

        // Eski davetlerin iptali, yeni davet ve e-posta (outbox) tek kayıtta yazılır.
        await _invites.AddAsync(invite, ct);

        return Result<ClientPortalInviteDto>.Success(ClientPortalInviteDto.From(invite, utcNow));
    }
}
