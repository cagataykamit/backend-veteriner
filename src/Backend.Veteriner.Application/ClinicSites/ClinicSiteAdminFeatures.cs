using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.ClinicSites;

/// <summary>
/// Destek: adres değiştirme hakkı biten siteye ek hak tanır (ADR-045 K10; yalnız platform yöneticisi, kiracılar arası). Site güncel ya
/// da eski adresiyle bulunur. Denetim kaydına yazılır.
/// </summary>
public sealed record GrantClinicSiteAddressChangesCommand(string Slug, int AdditionalChanges)
    : IRequest<Result<ClinicSiteStateDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public const int MaxAdditionalChanges = 3;

    public string AuditAction => "ClinicSite.AddressChanges.Grant";
    public string? AuditTarget => $"Slug={Slug};Additional={AdditionalChanges}";
}

public sealed class GrantClinicSiteAddressChangesCommandValidator : AbstractValidator<GrantClinicSiteAddressChangesCommand>
{
    public GrantClinicSiteAddressChangesCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty();
        RuleFor(x => x.AdditionalChanges).InclusiveBetween(1, GrantClinicSiteAddressChangesCommand.MaxAdditionalChanges);
    }
}

public sealed class GrantClinicSiteAddressChangesCommandHandler
    : IRequestHandler<GrantClinicSiteAddressChangesCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicSiteAddressBook _addressBook;
    private readonly IRepository<ClinicSite> _sites;
    private readonly TimeProvider _timeProvider;

    public GrantClinicSiteAddressChangesCommandHandler(
        ClinicSiteAddressBook addressBook,
        IRepository<ClinicSite> sites,
        TimeProvider timeProvider)
    {
        _addressBook = addressBook;
        _sites = sites;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(GrantClinicSiteAddressChangesCommand request, CancellationToken ct)
    {
        var address = await _addressBook.FindAsync(ClinicSiteSlug.Normalize(request.Slug), ct);
        if (address is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByIdForAdminSpec(address.SiteId), ct);
        if (site is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        site.GrantAddressChanges(request.AdditionalChanges, _timeProvider.GetUtcNow().UtcDateTime);
        return await ClinicSiteWrites.SaveTrackedAsync(_sites, site, ct);
    }
}
