using Backend.Veteriner.Application.Clients.Deletion;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Pets.Deletion;

// Hasta yumuşak silme, geri yükleme ve "Silinenler" listesi (ADR-054; docs/CLIENT_PET_DELETION_API_CONTRACT.md §3).

/// <summary>DELETE /api/v1/pets/{id}.</summary>
public sealed record DeletePetCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Pet.Delete";
    public string? AuditTarget => $"PetId={Id}";
}

/// <summary>POST /api/v1/pets/{id}/restore.</summary>
public sealed record RestorePetCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Pet.Restore";
    public string? AuditTarget => $"PetId={Id}";
}

/// <summary>GET /api/v1/pets/deleted.</summary>
public sealed record GetDeletedPetsQuery(PageRequest PageRequest) : IRequest<Result<PagedResult<DeletedPetListItemDto>>>;

/// <param name="ClientIsDeleted">true ise hasta tek başına geri yüklenemez (<c>Pets.ClientDeleted</c>); önce müşteri geri yüklenir.</param>
public sealed record DeletedPetListItemDto(
    Guid Id,
    string Name,
    string? SpeciesName,
    Guid ClientId,
    string? ClientFullName,
    bool ClientIsDeleted,
    bool DeletedWithClient,
    DateTime DeletedAtUtc,
    Guid? DeletedByUserId,
    string? DeletedByUserName);

public sealed class DeletePetCommandHandler : IRequestHandler<DeletePetCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IRepository<Pet> _pets;
    private readonly RecordDeletionBlockerReader _blockers;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DeletePetCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IRepository<Pet> pets,
        RecordDeletionBlockerReader blockers,
        IPetIntegrationEventOutbox petEvents,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _clients = clients;
        _pets = pets;
        _blockers = blockers;
        _petEvents = petEvents;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(DeletePetCommand request, CancellationToken ct)
    {
        var writer = await ClientPetDeletionSupport.ResolveWriterAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!writer.IsSuccess)
            return Result.Failure(writer.Error);
        var (tenantId, userId) = writer.Value;

        var pet = await _pets.FirstOrDefaultAsync(PetsForDeletionSpec.ById(tenantId, request.Id), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");
        if (pet.IsDeleted)
            return Result.Success();

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var blockers = await _blockers.ForPetAsync(tenantId, pet.Id, utcNow, ct);
        var deleted = pet.Delete(userId, utcNow, blockers);
        if (!deleted.IsSuccess)
            return deleted;

        await ClientPetDeletionSupport.EnqueuePetAsync(_petEvents, pet, client, utcNow, ct);
        return await SaveAsync(_unitOfWork, ct);
    }

    internal static async Task<Result> SaveAsync(IUnitOfWork unitOfWork, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                "Pets.ConcurrencyConflict",
                "Hayvan kaydı başka biri tarafından değiştirildi; sayfayı yenileyip tekrar deneyin.");
        }

        return Result.Success();
    }
}

public sealed class RestorePetCommandHandler : IRequestHandler<RestorePetCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Client> _clients;
    private readonly IRepository<Pet> _pets;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RestorePetCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<Client> clients,
        IRepository<Pet> pets,
        IPetIntegrationEventOutbox petEvents,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _tenants = tenants;
        _clients = clients;
        _pets = pets;
        _petEvents = petEvents;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RestorePetCommand request, CancellationToken ct)
    {
        var writer = await ClientPetDeletionSupport.ResolveWriterAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!writer.IsSuccess)
            return Result.Failure(writer.Error);
        var tenantId = writer.Value.TenantId;

        var pet = await _pets.FirstOrDefaultAsync(PetsForDeletionSpec.ById(tenantId, request.Id), ct);
        if (pet is null)
            return Result.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");
        if (!pet.IsDeleted)
            return Result.Success();

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        if (client is null)
            return Result.Failure("Clients.NotFound", "Müşteri bulunamadı.");

        // Silinmişken aynı müşteriye aynı ad ve türde yeni hasta açılmış olabilir; yeni kayıttaki mükerrer kuralı uygulanır.
        var duplicate = await _pets.FirstOrDefaultAsync(
            new PetByClientNameAndSpeciesIdExcludingIdSpec(pet.ClientId, Pet.NormalizeNameForDuplicateCheck(pet.Name), pet.SpeciesId, pet.Id), ct);
        if (duplicate is not null)
            return Result.Failure("Pets.DuplicatePet", "Bu müşterinin aynı ad ve türde aktif bir hastası var; geri yüklemek yerine o kaydı kullanın.");

        var restored = pet.Restore(client.IsDeleted);
        if (!restored.IsSuccess)
            return restored;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        await ClientPetDeletionSupport.EnqueuePetAsync(_petEvents, pet, client, utcNow, ct);
        return await DeletePetCommandHandler.SaveAsync(_unitOfWork, ct);
    }
}

public sealed class GetDeletedPetsQueryValidator : AbstractValidator<GetDeletedPetsQuery>
{
    public GetDeletedPetsQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);
    }
}

public sealed class GetDeletedPetsQueryHandler
    : IRequestHandler<GetDeletedPetsQuery, Result<PagedResult<DeletedPetListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IClinicVeterinarianReader _userNames;

    public GetDeletedPetsQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IClinicVeterinarianReader userNames)
    {
        _tenantContext = tenantContext;
        _pets = pets;
        _clients = clients;
        _userNames = userNames;
    }

    public async ValueTask<Result<PagedResult<DeletedPetListItemDto>>> Handle(GetDeletedPetsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<DeletedPetListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var term = DailySearchTerm.Create(request.PageRequest.Search);

        var total = await _pets.CountAsync(new DeletedPetsSpec(tenantId, term, null), ct);
        var rows = total == 0 ? [] : await _pets.ListAsync(new DeletedPetsSpec(tenantId, term, (page, pageSize)), ct);

        var clientIds = rows.Select(r => r.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? new Dictionary<Guid, Client>()
            : (await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct)).ToDictionary(c => c.Id);

        var userIds = rows.Where(r => r.DeletedByUserId.HasValue).Select(r => r.DeletedByUserId!.Value).Distinct().ToArray();
        var names = userIds.Length == 0 ? new Dictionary<Guid, string?>() : await _userNames.GetNamesAsync(userIds, ct);

        var items = rows
            .Select(r =>
            {
                var client = clients.GetValueOrDefault(r.ClientId);
                return new DeletedPetListItemDto(
                    r.Id,
                    r.Name,
                    r.SpeciesName,
                    r.ClientId,
                    client?.FullName,
                    client?.IsDeleted ?? false,
                    r.DeletedWithClient,
                    r.DeletedAtUtc,
                    r.DeletedByUserId,
                    r.DeletedByUserId is { } by ? names.GetValueOrDefault(by) : null);
            })
            .ToList();

        return Result<PagedResult<DeletedPetListItemDto>>.Success(PagedResult<DeletedPetListItemDto>.Create(items, total, page, pageSize));
    }
}
