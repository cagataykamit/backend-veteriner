using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Clients.Deletion;

// Müşteri yumuşak silme, geri yükleme ve "Silinenler" listesi (ADR-054; docs/CLIENT_PET_DELETION_API_CONTRACT.md §2).

/// <summary>DELETE /api/v1/clients/{id}: müşteri ve silinmemiş hastaları tek işlemde silinir; portal bağları kapanır.</summary>
public sealed record DeleteClientCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Client.Delete";
    public string? AuditTarget => $"ClientId={Id}";
}

/// <summary>POST /api/v1/clients/{id}/restore: müşteri ve onunla birlikte silinen hastalar geri gelir.</summary>
public sealed record RestoreClientCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Client.Restore";
    public string? AuditTarget => $"ClientId={Id}";
}

/// <summary>GET /api/v1/clients/deleted.</summary>
public sealed record GetDeletedClientsQuery(PageRequest PageRequest) : IRequest<Result<PagedResult<DeletedClientListItemDto>>>;

public sealed record DeletedClientListItemDto(
    Guid Id,
    string FullName,
    string? Phone,
    string? Email,
    DateTime DeletedAtUtc,
    Guid? DeletedByUserId,
    string? DeletedByUserName);

public sealed class DeleteClientCommandHandler : IRequestHandler<DeleteClientCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Client> _clients;
    private readonly IRepository<Pet> _pets;
    private readonly IRepository<OwnerAccountClientLink> _links;
    private readonly IRepository<OwnerPortalInvite> _invites;
    private readonly RecordDeletionBlockerReader _blockers;
    private readonly IClientIntegrationEventOutbox _clientEvents;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DeleteClientCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        IRepository<Client> clients,
        IRepository<Pet> pets,
        IRepository<OwnerAccountClientLink> links,
        IRepository<OwnerPortalInvite> invites,
        RecordDeletionBlockerReader blockers,
        IClientIntegrationEventOutbox clientEvents,
        IPetIntegrationEventOutbox petEvents,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _clients = clients;
        _pets = pets;
        _links = links;
        _invites = invites;
        _blockers = blockers;
        _clientEvents = clientEvents;
        _petEvents = petEvents;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(DeleteClientCommand request, CancellationToken ct)
    {
        var writer = await ClientPetDeletionSupport.ResolveWriterAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!writer.IsSuccess)
            return Result.Failure(writer.Error);
        var (tenantId, userId) = writer.Value;

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Müşteri bulunamadı.");
        if (client.IsDeleted)
            return Result.Success();

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var activePets = (await _pets.ListAsync(PetsForDeletionSpec.ByClient(tenantId, client.Id), ct))
            .Where(p => !p.IsDeleted)
            .ToList();
        var blockers = await _blockers.ForClientAsync(tenantId, client.Id, activePets.Select(p => p.Id).ToArray(), utcNow, ct);

        var deleted = client.Delete(userId, utcNow, blockers);
        if (!deleted.IsSuccess)
            return deleted;

        foreach (var pet in activePets)
        {
            pet.DeleteWithClient(userId, utcNow);
            await ClientPetDeletionSupport.EnqueuePetAsync(_petEvents, pet, client, utcNow, ct);
        }

        foreach (var link in await _links.ListAsync(new ActiveOwnerAccountClientLinksForCloseByClientSpec(tenantId, client.Id), ct))
            link.CloseByClientDeletion(userId, utcNow);
        foreach (var invite in await _invites.ListAsync(new PendingOwnerPortalInvitesByClientSpec(tenantId, client.Id), ct))
            invite.Revoke(userId, utcNow);

        await ClientPetDeletionSupport.EnqueueClientAsync(_clientEvents, client, utcNow, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Clients.ConcurrencyConflict",
                "Müşteri kaydı başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result.Success();
    }
}

public sealed class RestoreClientCommandHandler : IRequestHandler<RestoreClientCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<Client> _clients;
    private readonly IRepository<Pet> _pets;
    private readonly IClientIntegrationEventOutbox _clientEvents;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RestoreClientCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        IRepository<Client> clients,
        IRepository<Pet> pets,
        IClientIntegrationEventOutbox clientEvents,
        IPetIntegrationEventOutbox petEvents,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _clients = clients;
        _pets = pets;
        _clientEvents = clientEvents;
        _petEvents = petEvents;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RestoreClientCommand request, CancellationToken ct)
    {
        var writer = await ClientPetDeletionSupport.ResolveWriterAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!writer.IsSuccess)
            return Result.Failure(writer.Error);
        var tenantId = writer.Value.TenantId;

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.Id), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Müşteri bulunamadı.");
        if (!client.IsDeleted)
            return Result.Success();

        if (await FindConflictingClientAsync(tenantId, client, ct) is { } conflictingId)
        {
            return Result.Failure(new Error(
                "Clients.RestoreConflict",
                "Aynı ad, e-posta ya da telefonla aktif bir müşteri var; geri yüklemek yerine o kaydı kullanın ya da bilgileri düzenleyin.",
                new Dictionary<string, object?> { ["conflictingClientId"] = conflictingId }));
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        client.Restore(utcNow);

        foreach (var pet in await _pets.ListAsync(PetsForDeletionSpec.ByClient(tenantId, client.Id), ct))
        {
            if (!pet.IsDeleted || !pet.DeletedWithClient)
                continue;
            pet.RestoreWithClient();
            await ClientPetDeletionSupport.EnqueuePetAsync(_petEvents, pet, client, utcNow, ct);
        }

        await ClientPetDeletionSupport.EnqueueClientAsync(_clientEvents, client, utcNow, ct);

        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Clients.ConcurrencyConflict",
                "Müşteri kaydı başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Yeni kayıttaki mükerrer kuralları (ad + e-posta, ad + telefon) ve benzersiz e-posta + telefon indeksi; silinmiş
    /// müşteri bu sorgulara girmediği için kaydın kendisi çakışma sayılmaz.
    /// </summary>
    private async Task<Guid?> FindConflictingClientAsync(Guid tenantId, Client client, CancellationToken ct)
    {
        var nameKey = Client.NormalizeFullNameForDuplicateCheck(client.FullName);
        if (client.Email is { } email)
        {
            if (client.PhoneNormalized is { } phone
                && await _clients.FirstOrDefaultAsync(new ActiveClientByEmailAndPhoneSpec(tenantId, email, phone), ct) is { } byIndex)
                return byIndex.Id;
            if (await _clients.FirstOrDefaultAsync(new ClientByTenantNormalizedFullNameAndEmailSpec(tenantId, nameKey, email), ct) is { } byEmail)
                return byEmail.Id;
        }

        if (client.PhoneNormalized is { } phoneNorm
            && await _clients.FirstOrDefaultAsync(new ClientByTenantNormalizedFullNameAndPhoneSpec(tenantId, nameKey, phoneNorm), ct) is { } byPhone)
            return byPhone.Id;

        return null;
    }
}

public sealed class GetDeletedClientsQueryValidator : AbstractValidator<GetDeletedClientsQuery>
{
    public GetDeletedClientsQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);
    }
}

public sealed class GetDeletedClientsQueryHandler
    : IRequestHandler<GetDeletedClientsQuery, Result<PagedResult<DeletedClientListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Client> _clients;
    private readonly IClinicVeterinarianReader _userNames;

    public GetDeletedClientsQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Client> clients,
        IClinicVeterinarianReader userNames)
    {
        _tenantContext = tenantContext;
        _clients = clients;
        _userNames = userNames;
    }

    public async ValueTask<Result<PagedResult<DeletedClientListItemDto>>> Handle(GetDeletedClientsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<DeletedClientListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var term = DailySearchTerm.Create(request.PageRequest.Search);

        var total = await _clients.CountAsync(new DeletedClientsSpec(tenantId, term, null), ct);
        var rows = total == 0 ? [] : await _clients.ListAsync(new DeletedClientsSpec(tenantId, term, (page, pageSize)), ct);

        var userIds = rows.Where(r => r.DeletedByUserId.HasValue).Select(r => r.DeletedByUserId!.Value).Distinct().ToArray();
        var names = userIds.Length == 0 ? new Dictionary<Guid, string?>() : await _userNames.GetNamesAsync(userIds, ct);

        var items = rows
            .Select(r => new DeletedClientListItemDto(
                r.Id,
                r.FullName,
                r.Phone,
                r.Email,
                r.DeletedAtUtc,
                r.DeletedByUserId,
                r.DeletedByUserId is { } by ? names.GetValueOrDefault(by) : null))
            .ToList();

        return Result<PagedResult<DeletedClientListItemDto>>.Success(PagedResult<DeletedClientListItemDto>.Create(items, total, page, pageSize));
    }
}
