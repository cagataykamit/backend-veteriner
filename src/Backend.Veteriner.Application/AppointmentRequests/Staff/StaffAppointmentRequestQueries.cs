using Ardalis.Specification;
using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.AppointmentRequests.Staff;

/// <summary>
/// Talep kuyruğu (APPT-010) ve takvim katmanı (APPT-016): durum, klinik ve istenen saat aralığıyla. Aktif klinik bağlamı varsa
/// o klinik; yoksa kullanıcının erişebildiği klinikler. <c>AppointmentRequests.Manage</c> gerekir.
/// </summary>
public sealed record GetAppointmentRequestsQuery(
    Guid? ClinicId,
    AppointmentRequestStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize)
    : IRequest<Result<PagedResult<AppointmentRequestListItemDto>>>;

/// <summary>Talep detayı: geçmiş, müşteri eşleştirme önerileri ve istenen saatte boş uygun hekimler.</summary>
public sealed record GetAppointmentRequestQuery(Guid Id) : IRequest<Result<AppointmentRequestDetailDto>>;

public sealed class GetAppointmentRequestsQueryValidator : AbstractValidator<GetAppointmentRequestsQuery>
{
    public const int MaxPageSize = 500;

    public GetAppointmentRequestsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x).Must(x => x.FromUtc is null || x.ToUtc is null || x.FromUtc < x.ToUtc)
            .WithMessage("Başlangıç bitişten önce olmalıdır.");
    }
}

public sealed class GetAppointmentRequestsQueryHandler
    : IRequestHandler<GetAppointmentRequestsQuery, Result<PagedResult<AppointmentRequestListItemDto>>>
{
    private readonly StaffAppointmentRequestAccess _access;
    private readonly IReadRepository<AppointmentRequest> _requests;
    private readonly StaffAppointmentRequestMapper _mapper;

    public GetAppointmentRequestsQueryHandler(
        StaffAppointmentRequestAccess access,
        IReadRepository<AppointmentRequest> requests,
        StaffAppointmentRequestMapper mapper)
    {
        _access = access;
        _requests = requests;
        _mapper = mapper;
    }

    public async ValueTask<Result<PagedResult<AppointmentRequestListItemDto>>> Handle(GetAppointmentRequestsQuery request, CancellationToken ct)
    {
        var scope = await _access.ResolveListScopeAsync(request.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<AppointmentRequestListItemDto>>.Failure(scope.Error);
        var (tenantId, clinicIds) = scope.Value;

        var total = await _requests.CountAsync(
            new StaffAppointmentRequestsSpec(tenantId, clinicIds, request.Status, request.FromUtc, request.ToUtc), ct);
        var rows = total == 0
            ? []
            : await _requests.ListAsync(
                new StaffAppointmentRequestsSpec(tenantId, clinicIds, request.Status, request.FromUtc, request.ToUtc, request.Page, request.PageSize), ct);

        var items = await _mapper.ToListItemsAsync(tenantId, rows, ct);
        return Result<PagedResult<AppointmentRequestListItemDto>>.Success(
            PagedResult<AppointmentRequestListItemDto>.Create(items, total, request.Page, request.PageSize));
    }
}

public sealed class GetAppointmentRequestQueryHandler : IRequestHandler<GetAppointmentRequestQuery, Result<AppointmentRequestDetailDto>>
{
    private readonly StaffAppointmentRequestAccess _access;
    private readonly StaffAppointmentRequestMapper _mapper;
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly AppointmentAvailabilityService _availability;
    private readonly TimeProvider _timeProvider;

    public GetAppointmentRequestQueryHandler(
        StaffAppointmentRequestAccess access,
        StaffAppointmentRequestMapper mapper,
        IReadRepository<AppointmentReason> reasons,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IClinicVeterinarianReader veterinarians,
        AppointmentAvailabilityService availability,
        TimeProvider timeProvider)
    {
        _access = access;
        _mapper = mapper;
        _reasons = reasons;
        _clients = clients;
        _pets = pets;
        _veterinarians = veterinarians;
        _availability = availability;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestDetailDto>> Handle(GetAppointmentRequestQuery request, CancellationToken ct)
    {
        var found = await _access.FindAsync(request.Id, tracking: false, ct);
        if (!found.IsSuccess)
            return Result<AppointmentRequestDetailDto>.Failure(found.Error);
        var r = found.Value!;

        var summary = (await _mapper.ToListItemsAsync(r.TenantId, [r], ct))[0];
        var actorIds = r.History.Where(h => h.ActorUserId is not null).Select(h => h.ActorUserId!.Value).Distinct().ToList();
        var names = await _veterinarians.GetNamesAsync(actorIds, ct);
        var history = r.History
            .OrderBy(h => h.AtUtc)
            .Select(h => new AppointmentRequestHistoryDto(
                h.FromStatus, h.ToStatus, h.ActorKind, h.ActorUserId,
                h.ActorUserId is { } id ? names.GetValueOrDefault(id) : null, h.Note, h.AtUtc))
            .ToList();

        var matches = r.ClientId is null ? await FindClientMatchesAsync(r, ct) : [];

        var freeSlot = false;
        IReadOnlyList<Guid> freeVets = [];
        if (r.IsPending)
        {
            // Nedensiz ("Diğer / emin değilim") talepte müsaitlik sabit süre ve tüm hekimlerle hesaplanır (ADR-042 ek md. 1).
            var reason = r.ReasonId is { } reasonId
                ? await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(r.TenantId, r.ClinicId, reasonId), ct)
                : null;
            if (r.ReasonId is null || reason is not null)
            {
                var slot = await _availability.FindSlotAsync(
                    r.TenantId, r.ClinicId, reason, AppointmentRequest.DurationFor(reason), r.RequestedStartUtc,
                    _timeProvider.GetUtcNow().UtcDateTime, DateTime.MaxValue, ct);
                freeSlot = slot is not null;
                freeVets = slot?.FreeVeterinarianIds ?? [];
            }
        }

        return Result<AppointmentRequestDetailDto>.Success(new AppointmentRequestDetailDto(
            summary,
            r.PetSpecies,
            r.PetBreed,
            r.OwnerNote,
            r.ApprovedStartUtc,
            r.ApprovedVeterinarianUserId,
            r.WasRescheduled,
            r.RejectionReason,
            r.ClosedAtUtc,
            history,
            matches,
            freeSlot,
            freeVets));
    }

    private async Task<IReadOnlyList<ClientMatchDto>> FindClientMatchesAsync(AppointmentRequest r, CancellationToken ct)
    {
        var clients = await _clients.ListAsync(new ClientMatchesSpec(r.TenantId, r.ContactEmail, r.ContactPhone), ct);
        var result = new List<ClientMatchDto>();
        foreach (var c in clients)
        {
            var pets = await _pets.ListAsync(new PortalPetNamesByClientSpec(r.TenantId, c.Id), ct);
            result.Add(new ClientMatchDto(c.Id, c.FullName, c.Phone, c.Email, pets.Select(p => new ClientMatchPetDto(p.Id, p.Name)).ToList()));
        }

        return result;
    }
}

/// <summary>Onayda eşleştirme önerisi: aynı e-posta ya da telefon (müşteri kaydıyla aynı normalleştirme), en çok 5.</summary>
public sealed class ClientMatchesSpec : Specification<Client>
{
    public ClientMatchesSpec(Guid tenantId, string email, string? phoneNormalized)
        => Query.AsNoTracking()
            .Where(c => c.TenantId == tenantId
                        && !c.IsWalkIn
                        && (c.Email == email || (phoneNormalized != null && c.PhoneNormalized == phoneNormalized)))
            .OrderBy(c => c.FullName)
            .Take(5);
}

/// <summary>Kuyruk satırlarına müşteri ve hayvan adlarını ekler.</summary>
public sealed class StaffAppointmentRequestMapper
{
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;

    public StaffAppointmentRequestMapper(IReadRepository<Client> clients, IReadRepository<Pet> pets)
    {
        _clients = clients;
        _pets = pets;
    }

    public async Task<IReadOnlyList<AppointmentRequestListItemDto>> ToListItemsAsync(
        Guid tenantId,
        IReadOnlyList<AppointmentRequest> rows,
        CancellationToken ct)
    {
        var clientIds = rows.Where(r => r.ClientId is not null).Select(r => r.ClientId!.Value).Distinct().ToArray();
        var petIds = rows.Where(r => r.PetId is not null).Select(r => r.PetId!.Value).Distinct().ToArray();
        var clientNames = clientIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clients.ListAsync(new PortalClientsByIdsSpec(clientIds), ct)).Where(c => c.TenantId == tenantId).ToDictionary(c => c.Id, c => c.FullName);
        var petNames = petIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _pets.ListAsync(new PetsByIdsSpec(tenantId, petIds), ct)).ToDictionary(p => p.Id, p => p.Name);

        return rows.Select(r => new AppointmentRequestListItemDto(
                r.Id,
                r.ClinicId,
                r.Status,
                r.Source,
                r.ReasonId,
                r.ReasonName,
                r.RequestedStartUtc,
                r.DurationMinutes,
                r.ContactName,
                r.ContactEmail,
                r.ContactPhone,
                r.ClientId,
                r.ClientId is { } cid ? clientNames.GetValueOrDefault(cid) : null,
                r.PetId,
                r.PetId is { } pid ? petNames.GetValueOrDefault(pid) ?? r.PetName : r.PetName,
                r.CreatedAtUtc,
                r.AppointmentId))
            .ToList();
    }
}

public sealed class PetsByIdsSpec : Specification<Pet>
{
    public PetsByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        var array = ids.ToArray();
        Query.AsNoTracking().Where(p => p.TenantId == tenantId && array.Contains(p.Id));
    }
}

/// <summary>
/// Personel talep erişimi — tek yer: kiracı bağlamı, aktif klinik bağlamıyla uyum ve klinik ataması (atanmamış klinik talebi
/// yokmuş gibi 404).
/// </summary>
public sealed class StaffAppointmentRequestAccess
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IRepository<AppointmentRequest> _requests;
    private readonly IReadRepository<AppointmentRequest> _requestsRead;

    public StaffAppointmentRequestAccess(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IRepository<AppointmentRequest> requests,
        IReadRepository<AppointmentRequest> requestsRead)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _requests = requests;
        _requestsRead = requestsRead;
    }

    /// <summary>Liste kapsamı: (kiracı, klinik kümesi; null = kiracı geneli).</summary>
    public async Task<Result<(Guid TenantId, IReadOnlyCollection<Guid>? ClinicIds)>> ResolveListScopeAsync(Guid? requestClinicId, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<(Guid, IReadOnlyCollection<Guid>?)>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");
        if (ClinicContextRule.Conflicts(requestClinicId, _clinicContext.ClinicId))
            return Result<(Guid, IReadOnlyCollection<Guid>?)>.Failure(ClinicContextRule.Mismatch("AppointmentRequests"));

        var scope = await _scopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId ?? requestClinicId, ct);
        if (!scope.IsSuccess)
            return Result<(Guid, IReadOnlyCollection<Guid>?)>.Failure(scope.Error);

        IReadOnlyCollection<Guid>? clinicIds = scope.Value!.SingleClinicId is { } single
            ? [single]
            : scope.Value.AccessibleClinicIds;
        return Result<(Guid, IReadOnlyCollection<Guid>?)>.Success((tenantId, clinicIds));
    }

    /// <summary>Tek talep; kapsam dışıysa 404 <c>AppointmentRequests.NotFound</c>.</summary>
    public async Task<Result<AppointmentRequest>> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<AppointmentRequest>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var spec = new AppointmentRequestByIdSpec(tenantId, id);
        var row = tracking
            ? await _requests.FirstOrDefaultAsync(spec, ct)
            : await _requestsRead.FirstOrDefaultAsync(spec, ct);
        if (row is null || (_clinicContext.ClinicId is { } active && row.ClinicId != active))
            return NotFound();

        var scope = await _scopeResolver.ResolveAsync(tenantId, row.ClinicId, ct);
        return scope.IsSuccess ? Result<AppointmentRequest>.Success(row) : NotFound();
    }

    private static Result<AppointmentRequest> NotFound()
        => Result<AppointmentRequest>.Failure("AppointmentRequests.NotFound", "Randevu talebi bulunamadı.");
}
