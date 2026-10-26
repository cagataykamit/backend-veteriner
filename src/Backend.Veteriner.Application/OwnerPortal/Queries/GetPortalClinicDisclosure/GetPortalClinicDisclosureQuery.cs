using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.KvkkFormTemplate;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalClinicDisclosure;

/// <summary>
/// Bağın kliniğinin KVKK aydınlatma metni (ADR-021/024; klinik paneli <c>kvkk-disclosure-template</c> kaydı, yoksa taslak
/// varsayılan). Yer tutucular sunucuda doldurulur; tarih İstanbul günüdür.
/// </summary>
public sealed record GetPortalClinicDisclosureQuery(Guid OwnerAccountId, Guid LinkId) : IRequest<Result<PortalClinicDisclosureDto>>;

public sealed class GetPortalClinicDisclosureQueryHandler
    : IRequestHandler<GetPortalClinicDisclosureQuery, Result<PortalClinicDisclosureDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<OwnerAccountClientLink> _links;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<ClinicKvkkFormTemplate> _templates;
    private readonly TimeProvider _timeProvider;

    public GetPortalClinicDisclosureQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IReadRepository<ClinicKvkkFormTemplate> templates,
        TimeProvider timeProvider)
    {
        _access = access;
        _links = links;
        _clinics = clinics;
        _clients = clients;
        _templates = templates;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalClinicDisclosureDto>> Handle(GetPortalClinicDisclosureQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PortalClinicDisclosureDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var link = await _links.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(scope.LinkId), ct);
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(scope.TenantId, scope.ClinicId), ct);
        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(scope.TenantId, scope.ClientId), ct);
        if (link is null || clinic is null || client is null)
            return Result<PortalClinicDisclosureDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        var template = await _templates.FirstOrDefaultAsync(
            new ClinicKvkkFormTemplateByClinicSpec(scope.TenantId, scope.ClinicId, ClinicKvkkFormKind.Disclosure), ct);
        var rawText = template?.Text ?? ClinicKvkkFormTemplateDefaults.TextFor(ClinicKvkkFormKind.Disclosure);
        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);

        var text = rawText
            .Replace(ClinicKvkkFormTemplateDefaults.ClinicNamePlaceholder, clinic.Name, StringComparison.Ordinal)
            .Replace(ClinicKvkkFormTemplateDefaults.ClientNamePlaceholder, client.FullName, StringComparison.Ordinal)
            .Replace(ClinicKvkkFormTemplateDefaults.DatePlaceholder, today.ToString("dd.MM.yyyy"), StringComparison.Ordinal);

        return Result<PortalClinicDisclosureDto>.Success(new PortalClinicDisclosureDto(
            link.Id,
            clinic.Id,
            clinic.Name,
            text,
            template is null,
            link.DisclosureAcknowledgedAtUtc));
    }
}
