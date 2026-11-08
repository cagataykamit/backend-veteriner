using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Waitlist;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Waitlist;

// ---------- DTO ----------

public sealed record WaitlistOfferDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistOfferStatus Status,
    Guid SourceAppointmentId,
    DateTime SlotStartUtc,
    int DurationMinutes,
    Guid? VeterinarianUserId,
    bool IsAutomatic,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ClosedAtUtc,
    Guid? AppointmentId);

/// <summary>
/// Bekleme kaydı. <see cref="Position"/>: klinikte sıradaki kayıtlar arasında 1'den başlayan sıra (kapanmış kayıtta boş).
/// <see cref="OfferBlocker"/>: teklif neden gidemez (e-posta yok, tercih kapalı); klinik uyarılır.
/// </summary>
public sealed record WaitlistEntryDto(
    Guid Id,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid PetId,
    string PetName,
    Guid? PreferredVeterinarianUserId,
    string? PreferredVeterinarianName,
    AppointmentType AppointmentType,
    Guid? ReasonId,
    string? ReasonName,
    int? DurationMinutes,
    DateOnly EarliestDate,
    DateOnly LatestDate,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistWeekdays PreferredDays,
    TimeOnly? PreferredFromTime,
    TimeOnly? PreferredToTime,
    string? Note,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistEntryStatus Status,
    int? Position,
    bool IsPastRange,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistOfferBlocker OfferBlocker,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc,
    string? CloseReason,
    Guid? BookedAppointmentId,
    WaitlistOfferDto? OpenOffer);

public sealed record WaitlistEntryEventDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistEntryEventType Type,
    DateTime OccurredAtUtc,
    Guid? ActorUserId,
    string? ActorName,
    Guid? OfferId,
    DateTime? OfferSlotStartUtc);

public sealed record WaitlistEntryDetailDto(WaitlistEntryDto Entry, IReadOnlyList<WaitlistEntryEventDto> Events, IReadOnlyList<WaitlistOfferDto> Offers);

public sealed record WaitlistCandidateDto(
    Guid EntryId,
    int Position,
    string PetName,
    string ClientName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistOfferBlocker OfferBlocker);

/// <summary>
/// Boşalan saat ve ona uyan kayıtlar (iptal ekranı ve elle teklif): <see cref="EligibleCount"/> teklif alabilecek,
/// <see cref="BlockedCount"/> uyan ama e-postası ya da tercihi olmayan kayıt sayısı.
/// </summary>
public sealed record WaitlistSlotDto(
    Guid AppointmentId,
    DateTime StartUtc,
    int DurationMinutes,
    Guid? VeterinarianUserId,
    bool IsReleased,
    bool AutoOfferEnabled,
    int EligibleCount,
    int BlockedCount,
    WaitlistOfferDto? OpenOffer,
    bool IsBooked,
    IReadOnlyList<WaitlistCandidateDto> Candidates);

/// <summary>Bugün ekranı: klinikte sıradaki kayıt sayısı ve günün kalan saatlerinde boşalan saatler.</summary>
public sealed record WaitlistTodayDto(int WaitingCount, IReadOnlyList<WaitlistSlotDto> ReleasedSlots);

public enum WaitlistMoveDirection
{
    Up = 0,
    Down = 1,
}

// ---------- İstekler ----------

/// <summary>GET /api/v1/waitlist-entries. Her pakette okunur (paket düşünce kayıtlar görünür kalır).</summary>
public sealed record GetWaitlistEntriesQuery(Guid? ClinicId, Guid? VeterinarianUserId, WaitlistEntryStatus? Status)
    : IRequest<Result<IReadOnlyList<WaitlistEntryDto>>>;

/// <summary>GET /api/v1/waitlist-entries/{id}: kayıt, geçmiş ve teklifler.</summary>
public sealed record GetWaitlistEntryQuery(Guid Id) : IRequest<Result<WaitlistEntryDetailDto>>;

/// <summary>GET /api/v1/waitlist-entries/slots/{appointmentId}: boşalan saate uyan kayıtlar. Profesyonel ve Kurumsal.</summary>
public sealed record GetWaitlistSlotQuery(Guid AppointmentId) : IRequest<Result<WaitlistSlotDto>>;

/// <summary>GET /api/v1/waitlist-entries/today?clinicId=: Bugün ekranı bilgisi. Profesyonel ve Kurumsal.</summary>
public sealed record GetWaitlistTodayQuery(Guid ClinicId) : IRequest<Result<WaitlistTodayDto>>;

public sealed record CreateWaitlistEntryCommand(
    Guid ClinicId,
    Guid ClientId,
    Guid PetId,
    Guid? PreferredVeterinarianUserId,
    AppointmentType AppointmentType,
    Guid? ReasonId,
    int? DurationMinutes,
    DateOnly EarliestDate,
    DateOnly LatestDate,
    WaitlistWeekdays PreferredDays,
    TimeOnly? PreferredFromTime,
    TimeOnly? PreferredToTime,
    string? Note)
    : IRequest<Result<WaitlistEntryDto>>, IAuditableRequest
{
    public string AuditAction => "Waitlist.Entry.Create";
    public string? AuditTarget => $"ClinicId={ClinicId};PetId={PetId}";
}

/// <summary>Kaydı kapatır; açık teklif geri çekilir (kabul bağlantısı kapanır).</summary>
public sealed record CloseWaitlistEntryCommand(Guid Id, string? Reason) : IRequest<Result<WaitlistEntryDto>>, IAuditableRequest
{
    public string AuditAction => "Waitlist.Entry.Close";
    public string? AuditTarget => $"WaitlistEntryId={Id}";
}

/// <summary>Elle sıra: kayıt bir üstteki ya da alttaki kayıtla yer değiştirir.</summary>
public sealed record MoveWaitlistEntryCommand(Guid Id, WaitlistMoveDirection Direction) : IRequest<Result<WaitlistEntryDto>>, IAuditableRequest
{
    public string AuditAction => "Waitlist.Entry.Move";
    public string? AuditTarget => $"WaitlistEntryId={Id};Direction={Direction}";
}

/// <summary>Elle teklif: boşalan saat (iptal edilen randevu) seçilen kayda gönderilir. Otomatik teklif kapalıyken de çalışır.</summary>
public sealed record SendWaitlistOfferCommand(Guid EntryId, Guid SourceAppointmentId) : IRequest<Result<WaitlistOfferDto>>, IAuditableRequest
{
    public string AuditAction => "Waitlist.Offer.Send";
    public string? AuditTarget => $"WaitlistEntryId={EntryId};SourceAppointmentId={SourceAppointmentId}";
}

public sealed class CreateWaitlistEntryCommandValidator : AbstractValidator<CreateWaitlistEntryCommand>
{
    // Alan kuralları domain'de (WaitlistEntry.Create); burada yalnız zorunlu kimlikler.
    public CreateWaitlistEntryCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.PreferredVeterinarianUserId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.ReasonId).Must(id => id is null || id != Guid.Empty);
    }
}

// ---------- Ortak ----------

/// <summary>Panel uçlarının ortak kapısı ve DTO kurulumu (TIER-018).</summary>
public sealed class WaitlistAccess
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicReadScopeResolver _clinicScope;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IReadRepository<WaitlistEntry> _entries;
    private readonly IReadRepository<WaitlistOffer> _offers;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly TimeProvider _timeProvider;

    public WaitlistAccess(
        ITenantContext tenantContext,
        IClinicReadScopeResolver clinicScope,
        TenantPlanFeatureEvaluator planFeatures,
        IReadRepository<WaitlistEntry> entries,
        IReadRepository<WaitlistOffer> offers,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<AppointmentReason> reasons,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicScope = clinicScope;
        _planFeatures = planFeatures;
        _entries = entries;
        _offers = offers;
        _clients = clients;
        _pets = pets;
        _reasons = reasons;
        _veterinarians = veterinarians;
        _timeProvider = timeProvider;
    }

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Kiracı bağlamı; <paramref name="requireFeature"/> ise paket kapısı (403 <c>Subscriptions.FeatureNotInPlan</c>).</summary>
    public async Task<Result<Guid>> TenantAsync(bool requireFeature, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<Guid>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (requireFeature)
        {
            var allowed = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.Waitlist, ct);
            if (!allowed.IsSuccess)
                return Result<Guid>.Failure(allowed.Error);
        }

        return Result<Guid>.Success(tenantId);
    }

    public async Task<Result> EnsureClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var scope = await _clinicScope.ResolveAsync(tenantId, clinicId, ct);
        return scope.IsSuccess ? Result.Success() : Result.Failure(scope.Error);
    }

    /// <summary>Kayıt (izlenir) ve klinik erişimi; bulunamayan ya da erişilemeyen kayıtta 404.</summary>
    public async Task<Result<WaitlistEntry>> FindAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var entry = await _entries.FirstOrDefaultAsync(new WaitlistEntryByIdSpec(tenantId, id), ct);
        if (entry is null || !(await EnsureClinicAsync(tenantId, entry.ClinicId, ct)).IsSuccess)
            return Result<WaitlistEntry>.Failure(WaitlistErrors.NotFoundCode, "Bekleme kaydı bulunamadı.");
        return Result<WaitlistEntry>.Success(entry);
    }

    public static WaitlistOfferDto ToDto(WaitlistOffer o)
        => new(o.Id, o.Status, o.SourceAppointmentId, o.SlotStartUtc, o.DurationMinutes, o.VeterinarianUserId, o.IsAutomatic,
            o.CreatedAtUtc, o.ExpiresAtUtc, o.ClosedAtUtc, o.AppointmentId);

    public async Task<IReadOnlyList<WaitlistEntryDto>> ToDtosAsync(Guid tenantId, IReadOnlyList<WaitlistEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
            return [];

        var clients = (await _clients.ListAsync(new WaitlistClientsByIdsSpec(tenantId, entries.Select(e => e.ClientId).Distinct().ToArray()), ct))
            .ToDictionary(c => c.Id);
        var pets = (await _pets.ListAsync(new WaitlistPetsByIdsSpec(tenantId, entries.Select(e => e.PetId).Distinct().ToArray()), ct))
            .ToDictionary(p => p.Id);
        var openOffers = (await _offers.ListAsync(new WaitlistOffersByEntriesSpec(tenantId, entries.Select(e => e.Id).ToArray()), ct))
            .Where(o => o.IsOpen)
            .ToDictionary(o => o.WaitlistEntryId);
        var vetNames = await _veterinarians.GetNamesAsync(
            entries.Where(e => e.PreferredVeterinarianUserId is not null).Select(e => e.PreferredVeterinarianUserId!.Value).Distinct().ToArray(), ct);
        var reasonNames = new Dictionary<Guid, string>();
        foreach (var e in entries.Where(e => e.ReasonId is not null).DistinctBy(e => e.ReasonId))
        {
            if (await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(tenantId, e.ClinicId, e.ReasonId!.Value), ct) is { } reason)
                reasonNames[reason.Id] = reason.Name;
        }

        // Sıra numarası klinik başınadır: sıradaki kayıtları sıra anahtarıyla say.
        var positions = new Dictionary<Guid, int>();
        foreach (var clinicId in entries.Where(e => e.IsWaiting).Select(e => e.ClinicId).Distinct())
        {
            var waiting = await _entries.ListAsync(new WaitingEntriesByClinicSpec(tenantId, clinicId), ct);
            for (var i = 0; i < waiting.Count; i++)
                positions[waiting[i].Id] = i + 1;
        }

        var today = OperationDayBounds.ToLocalDate(UtcNow);
        return entries.Select(e =>
        {
            clients.TryGetValue(e.ClientId, out var client);
            pets.TryGetValue(e.PetId, out var pet);
            return new WaitlistEntryDto(
                e.Id, e.ClinicId, e.ClientId, client?.FullName ?? string.Empty, e.PetId, pet?.Name ?? string.Empty,
                e.PreferredVeterinarianUserId,
                e.PreferredVeterinarianUserId is { } v ? vetNames.GetValueOrDefault(v) : null,
                e.AppointmentType, e.ReasonId, e.ReasonId is { } r ? reasonNames.GetValueOrDefault(r) : null, e.DurationMinutes,
                e.EarliestDate, e.LatestDate, e.PreferredDays, e.PreferredFromTime, e.PreferredToTime, e.Note, e.Status,
                positions.TryGetValue(e.Id, out var p) ? p : null,
                e.IsPastRange(today),
                client is null ? WaitlistOfferBlocker.NoEmail : WaitlistOfferService.BlockerFor(client),
                e.CreatedAtUtc, e.ClosedAtUtc, e.CloseReason, e.BookedAppointmentId,
                openOffers.TryGetValue(e.Id, out var offer) ? ToDto(offer) : null);
        }).ToList();
    }

    public async Task<WaitlistEntryDto> ToDtoAsync(Guid tenantId, WaitlistEntry entry, CancellationToken ct)
        => (await ToDtosAsync(tenantId, [entry], ct))[0];
}

// ---------- İşleyiciler ----------

public sealed class GetWaitlistEntriesQueryHandler : IRequestHandler<GetWaitlistEntriesQuery, Result<IReadOnlyList<WaitlistEntryDto>>>
{
    /// <summary>Liste üst sınırı (çalışma varsayımı); sayfalama ilk dilimde yok.</summary>
    public const int MaxRows = 500;

    private readonly WaitlistAccess _access;
    private readonly IReadRepository<WaitlistEntry> _entries;

    public GetWaitlistEntriesQueryHandler(WaitlistAccess access, IReadRepository<WaitlistEntry> entries)
    {
        _access = access;
        _entries = entries;
    }

    public async ValueTask<Result<IReadOnlyList<WaitlistEntryDto>>> Handle(GetWaitlistEntriesQuery request, CancellationToken ct)
    {
        var tenant = await _access.TenantAsync(requireFeature: false, ct);
        if (!tenant.IsSuccess)
            return Result<IReadOnlyList<WaitlistEntryDto>>.Failure(tenant.Error);
        if (request.ClinicId is { } clinicId)
        {
            var clinic = await _access.EnsureClinicAsync(tenant.Value, clinicId, ct);
            if (!clinic.IsSuccess)
                return Result<IReadOnlyList<WaitlistEntryDto>>.Failure(clinic.Error);
        }

        var rows = await _entries.ListAsync(
            new WaitlistEntriesFilterSpec(tenant.Value, request.ClinicId, request.VeterinarianUserId, request.Status, MaxRows), ct);
        return Result<IReadOnlyList<WaitlistEntryDto>>.Success(await _access.ToDtosAsync(tenant.Value, rows, ct));
    }
}

public sealed class GetWaitlistEntryQueryHandler : IRequestHandler<GetWaitlistEntryQuery, Result<WaitlistEntryDetailDto>>
{
    private readonly WaitlistAccess _access;
    private readonly IReadRepository<WaitlistEntryEvent> _events;
    private readonly IReadRepository<WaitlistOffer> _offers;
    private readonly IClinicVeterinarianReader _users;

    public GetWaitlistEntryQueryHandler(
        WaitlistAccess access, IReadRepository<WaitlistEntryEvent> events, IReadRepository<WaitlistOffer> offers, IClinicVeterinarianReader users)
    {
        _access = access;
        _events = events;
        _offers = offers;
        _users = users;
    }

    public async ValueTask<Result<WaitlistEntryDetailDto>> Handle(GetWaitlistEntryQuery request, CancellationToken ct)
    {
        var tenant = await _access.TenantAsync(requireFeature: false, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistEntryDetailDto>.Failure(tenant.Error);
        var found = await _access.FindAsync(tenant.Value, request.Id, ct);
        if (!found.IsSuccess)
            return Result<WaitlistEntryDetailDto>.Failure(found.Error);

        var entry = found.Value!;
        var events = await _events.ListAsync(new WaitlistEventsByEntrySpec(tenant.Value, entry.Id), ct);
        var offers = await _offers.ListAsync(new WaitlistOffersByEntriesSpec(tenant.Value, [entry.Id]), ct);
        var offerById = offers.ToDictionary(o => o.Id);
        var names = await _users.GetNamesAsync(events.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value).Distinct().ToArray(), ct);

        return Result<WaitlistEntryDetailDto>.Success(new WaitlistEntryDetailDto(
            await _access.ToDtoAsync(tenant.Value, entry, ct),
            events.Select(e => new WaitlistEntryEventDto(
                e.Type, e.OccurredAtUtc, e.ActorUserId, e.ActorUserId is { } a ? names.GetValueOrDefault(a) : null, e.OfferId,
                e.OfferId is { } oid && offerById.TryGetValue(oid, out var o) ? o.SlotStartUtc : null)).ToList(),
            offers.Select(WaitlistAccess.ToDto).ToList()));
    }
}

public sealed class CreateWaitlistEntryCommandHandler : IRequestHandler<CreateWaitlistEntryCommand, Result<WaitlistEntryDto>>
{
    private readonly WaitlistAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<AppointmentReason> _reasons;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly IRepository<WaitlistEntry> _entries;
    private readonly IRepository<WaitlistEntryEvent> _events;

    public CreateWaitlistEntryCommandHandler(
        WaitlistAccess access,
        IClientContext clientContext,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<AppointmentReason> reasons,
        IClinicVeterinarianReader veterinarians,
        IRepository<WaitlistEntry> entries,
        IRepository<WaitlistEntryEvent> events)
    {
        _access = access;
        _clientContext = clientContext;
        _clinics = clinics;
        _clients = clients;
        _pets = pets;
        _reasons = reasons;
        _veterinarians = veterinarians;
        _entries = entries;
        _events = events;
    }

    public async ValueTask<Result<WaitlistEntryDto>> Handle(CreateWaitlistEntryCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");
        var tenant = await _access.TenantAsync(requireFeature: true, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(tenant.Error);
        var tenantId = tenant.Value;

        var clinicAccess = await _access.EnsureClinicAsync(tenantId, request.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(clinicAccess.Error);
        if (await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct) is not { IsActive: true })
            return Fail("Clinics.NotFound", "Klinik bulunamadı veya pasif.");

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct);
        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (client is null || pet is null || pet.ClientId != client.Id)
            return Fail("Pets.NotFound", "Hayvan bulunamadı veya seçilen müşteriye ait değil.");
        var care = pet.EnsureCanStartCare();
        if (!care.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(care.Error);

        if (request.PreferredVeterinarianUserId is { } vetId
            && !await _veterinarians.IsClinicVeterinarianAsync(vetId, tenantId, request.ClinicId, ct))
            return Fail(WaitlistEntry.ValidationCode, "İstenen hekim bu kliniğin aktif hekimi olmalıdır.");

        var duration = request.DurationMinutes;
        if (request.ReasonId is { } reasonId)
        {
            var reason = await _reasons.FirstOrDefaultAsync(new AppointmentReasonByIdSpec(tenantId, request.ClinicId, reasonId), ct);
            if (reason is not { IsActive: true })
                return Fail(WaitlistEntry.ValidationCode, "Randevu nedeni bulunamadı veya pasif.");
            duration ??= reason.DefaultDurationMinutes;
        }

        var utcNow = _access.UtcNow;
        var created = WaitlistEntry.Create(
            tenantId, request.ClinicId, client.Id, pet.Id, request.PreferredVeterinarianUserId, request.AppointmentType, request.ReasonId,
            duration, request.EarliestDate, request.LatestDate, request.PreferredDays, request.PreferredFromTime, request.PreferredToTime,
            request.Note, userId, OperationDayBounds.ToLocalDate(utcNow), utcNow);
        if (!created.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(created.Error);

        var entry = created.Value!;
        await _entries.AddAsync(entry, ct);
        await _events.AddAsync(WaitlistEntryEvent.Create(entry, WaitlistEntryEventType.Added, null, userId, utcNow), ct);
        await _entries.SaveChangesAsync(ct);
        return Result<WaitlistEntryDto>.Success(await _access.ToDtoAsync(tenantId, entry, ct));
    }

    private static Result<WaitlistEntryDto> Fail(string code, string message) => Result<WaitlistEntryDto>.Failure(code, message);
}

public sealed class CloseWaitlistEntryCommandHandler : IRequestHandler<CloseWaitlistEntryCommand, Result<WaitlistEntryDto>>
{
    private readonly WaitlistAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<WaitlistOffer> _offers;
    private readonly IRepository<AppointmentOwnerLink> _links;
    private readonly IRepository<WaitlistEntryEvent> _events;

    public CloseWaitlistEntryCommandHandler(
        WaitlistAccess access,
        IClientContext clientContext,
        IRepository<WaitlistOffer> offers,
        IRepository<AppointmentOwnerLink> links,
        IRepository<WaitlistEntryEvent> events)
    {
        _access = access;
        _clientContext = clientContext;
        _offers = offers;
        _links = links;
        _events = events;
    }

    public async ValueTask<Result<WaitlistEntryDto>> Handle(CloseWaitlistEntryCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Result<WaitlistEntryDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");
        var tenant = await _access.TenantAsync(requireFeature: false, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(tenant.Error);
        var found = await _access.FindAsync(tenant.Value, request.Id, ct);
        if (!found.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(found.Error);

        var entry = found.Value!;
        var utcNow = _access.UtcNow;
        var closed = entry.Close(userId, request.Reason, utcNow);
        if (!closed.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(closed.Error);

        // Açık teklif geri çekilir; kabul bağlantısı kapanır (kayıt kapandıktan sonra randevu oluşmaz).
        if (await _offers.FirstOrDefaultAsync(new OpenWaitlistOfferByEntrySpec(entry.TenantId, entry.Id), ct) is { } offer)
        {
            offer.Withdraw(utcNow);
            foreach (var link in await _links.ListAsync(new OpenWaitlistOfferLinksSpec(entry.TenantId, offer.Id), ct))
                link.Close(utcNow);
            await _events.AddAsync(WaitlistEntryEvent.Create(entry, WaitlistEntryEventType.OfferWithdrawn, offer.Id, userId, utcNow), ct);
        }

        await _events.AddAsync(WaitlistEntryEvent.Create(entry, WaitlistEntryEventType.Closed, null, userId, utcNow), ct);
        try
        {
            await _events.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<WaitlistEntryDto>.Failure(WaitlistErrors.ConcurrencyConflictCode, "Kayıt aynı anda değişti; listeyi yenileyip tekrar deneyin.");
        }

        return Result<WaitlistEntryDto>.Success(await _access.ToDtoAsync(tenant.Value, entry, ct));
    }
}

public sealed class MoveWaitlistEntryCommandHandler : IRequestHandler<MoveWaitlistEntryCommand, Result<WaitlistEntryDto>>
{
    private readonly WaitlistAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<WaitlistEntry> _entries;
    private readonly IRepository<WaitlistEntryEvent> _events;

    public MoveWaitlistEntryCommandHandler(
        WaitlistAccess access, IClientContext clientContext, IRepository<WaitlistEntry> entries, IRepository<WaitlistEntryEvent> events)
    {
        _access = access;
        _clientContext = clientContext;
        _entries = entries;
        _events = events;
    }

    public async ValueTask<Result<WaitlistEntryDto>> Handle(MoveWaitlistEntryCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Result<WaitlistEntryDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");
        var tenant = await _access.TenantAsync(requireFeature: true, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(tenant.Error);
        var found = await _access.FindAsync(tenant.Value, request.Id, ct);
        if (!found.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(found.Error);

        var entry = found.Value!;
        if (!entry.IsWaiting)
            return Result<WaitlistEntryDto>.Failure("Waitlist.EntryNotOpen", "Yalnız sıradaki kayıtların yeri değiştirilebilir.");

        var up = request.Direction == WaitlistMoveDirection.Up;
        var neighbour = await _entries.FirstOrDefaultAsync(new WaitlistNeighbourSpec(entry, up), ct);
        if (neighbour is null)
            return Result<WaitlistEntryDto>.Success(await _access.ToDtoAsync(tenant.Value, entry, ct));

        var swapped = entry.SwapOrderWith(neighbour);
        if (!swapped.IsSuccess)
            return Result<WaitlistEntryDto>.Failure(swapped.Error);

        await _events.AddAsync(WaitlistEntryEvent.Create(
            entry, up ? WaitlistEntryEventType.MovedUp : WaitlistEntryEventType.MovedDown, null, userId, _access.UtcNow), ct);
        try
        {
            await _entries.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<WaitlistEntryDto>.Failure(WaitlistErrors.ConcurrencyConflictCode, "Sıra aynı anda değişti; listeyi yenileyip tekrar deneyin.");
        }

        return Result<WaitlistEntryDto>.Success(await _access.ToDtoAsync(tenant.Value, entry, ct));
    }
}

public sealed class SendWaitlistOfferCommandHandler : IRequestHandler<SendWaitlistOfferCommand, Result<WaitlistOfferDto>>
{
    private readonly WaitlistAccess _access;
    private readonly IClientContext _clientContext;
    private readonly WaitlistOfferService _service;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IRepository<WaitlistOffer> _offers;

    public SendWaitlistOfferCommandHandler(
        WaitlistAccess access,
        IClientContext clientContext,
        WaitlistOfferService service,
        IReadRepository<Appointment> appointments,
        IReadRepository<Clinic> clinics,
        IRepository<WaitlistOffer> offers)
    {
        _access = access;
        _clientContext = clientContext;
        _service = service;
        _appointments = appointments;
        _clinics = clinics;
        _offers = offers;
    }

    public async ValueTask<Result<WaitlistOfferDto>> Handle(SendWaitlistOfferCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
            return Result<WaitlistOfferDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");
        var tenant = await _access.TenantAsync(requireFeature: true, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistOfferDto>.Failure(tenant.Error);
        var found = await _access.FindAsync(tenant.Value, request.EntryId, ct);
        if (!found.IsSuccess)
            return Result<WaitlistOfferDto>.Failure(found.Error);
        var entry = found.Value!;

        var source = await _appointments.FirstOrDefaultAsync(new AppointmentByIdSpec(tenant.Value, request.SourceAppointmentId), ct);
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenant.Value, entry.ClinicId), ct);
        if (source is null || clinic is not { IsActive: true })
            return Result<WaitlistOfferDto>.Failure(WaitlistErrors.SlotNotReleasedCode, "Bu saat boşalmış bir randevu saati değil.");

        var utcNow = _access.UtcNow;
        var candidate = (await _service.ListCandidatesAsync(source, utcNow, ct)).FirstOrDefault(c => c.Entry.Id == entry.Id);
        if (candidate is null)
        {
            return Result<WaitlistOfferDto>.Failure(
                "WaitlistOffers.EntryNotMatching", "Kayıt bu saate uymuyor ya da bu saat için daha önce teklif aldı.");
        }

        var sent = await _service.SendAsync(candidate, source, clinic, isAutomatic: false, userId, utcNow, ct);
        if (!sent.IsSuccess)
            return Result<WaitlistOfferDto>.Failure(sent.Error);

        try
        {
            await _offers.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Süzülmüş benzersiz indeks: aynı anda saat ya da kayıt için başka teklif açıldı.
            return Result<WaitlistOfferDto>.Failure(WaitlistErrors.OfferAlreadyOpenCode, "Bu saat ya da kayıt için aynı anda başka bir teklif açıldı.");
        }

        return Result<WaitlistOfferDto>.Success(WaitlistAccess.ToDto(sent.Value!));
    }
}

public sealed class GetWaitlistSlotQueryHandler : IRequestHandler<GetWaitlistSlotQuery, Result<WaitlistSlotDto>>
{
    private readonly WaitlistAccess _access;
    private readonly WaitlistSlotReader _slots;
    private readonly IReadRepository<Appointment> _appointments;

    public GetWaitlistSlotQueryHandler(WaitlistAccess access, WaitlistSlotReader slots, IReadRepository<Appointment> appointments)
    {
        _access = access;
        _slots = slots;
        _appointments = appointments;
    }

    public async ValueTask<Result<WaitlistSlotDto>> Handle(GetWaitlistSlotQuery request, CancellationToken ct)
    {
        var tenant = await _access.TenantAsync(requireFeature: true, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistSlotDto>.Failure(tenant.Error);

        var appointment = await _appointments.FirstOrDefaultAsync(new AppointmentByIdSpec(tenant.Value, request.AppointmentId), ct);
        if (appointment is null || !(await _access.EnsureClinicAsync(tenant.Value, appointment.ClinicId, ct)).IsSuccess)
            return Result<WaitlistSlotDto>.Failure("Appointments.NotFound", "Randevu bulunamadı veya kiracıya ait değil.");

        return Result<WaitlistSlotDto>.Success(await _slots.ReadAsync(appointment, _access.UtcNow, ct));
    }
}

public sealed class GetWaitlistTodayQueryHandler : IRequestHandler<GetWaitlistTodayQuery, Result<WaitlistTodayDto>>
{
    private readonly WaitlistAccess _access;
    private readonly WaitlistSlotReader _slots;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<WaitlistEntry> _entries;

    public GetWaitlistTodayQueryHandler(
        WaitlistAccess access, WaitlistSlotReader slots, IReadRepository<Appointment> appointments, IReadRepository<WaitlistEntry> entries)
    {
        _access = access;
        _slots = slots;
        _appointments = appointments;
        _entries = entries;
    }

    public async ValueTask<Result<WaitlistTodayDto>> Handle(GetWaitlistTodayQuery request, CancellationToken ct)
    {
        var tenant = await _access.TenantAsync(requireFeature: true, ct);
        if (!tenant.IsSuccess)
            return Result<WaitlistTodayDto>.Failure(tenant.Error);
        var clinic = await _access.EnsureClinicAsync(tenant.Value, request.ClinicId, ct);
        if (!clinic.IsSuccess)
            return Result<WaitlistTodayDto>.Failure(clinic.Error);

        var utcNow = _access.UtcNow;
        var (_, dayEndUtc) = OperationDayBounds.ForUtcNow(utcNow);
        var released = await _appointments.ListAsync(new ReleasedAppointmentsInRangeSpec(tenant.Value, request.ClinicId, utcNow, dayEndUtc), ct);
        var slots = new List<WaitlistSlotDto>(released.Count);
        foreach (var appointment in released)
            slots.Add(await _slots.ReadAsync(appointment, utcNow, ct));

        var waiting = await _entries.CountAsync(new WaitingEntriesByClinicSpec(tenant.Value, request.ClinicId), ct);
        return Result<WaitlistTodayDto>.Success(new WaitlistTodayDto(waiting, slots));
    }
}

/// <summary>Boşalan saatin bekleme listesi özeti (iptal ekranı ve Bugün aynı kuraldan okur).</summary>
public sealed class WaitlistSlotReader
{
    private readonly WaitlistOfferService _service;
    private readonly IReadRepository<WaitlistOffer> _offers;
    private readonly IReadRepository<ClinicAppointmentSettings> _settings;
    private readonly IReadRepository<WaitlistEntry> _entries;

    public WaitlistSlotReader(
        WaitlistOfferService service,
        IReadRepository<WaitlistOffer> offers,
        IReadRepository<ClinicAppointmentSettings> settings,
        IReadRepository<WaitlistEntry> entries)
    {
        _service = service;
        _offers = offers;
        _settings = settings;
        _entries = entries;
    }

    public async Task<WaitlistSlotDto> ReadAsync(Appointment appointment, DateTime utcNow, CancellationToken ct)
    {
        var settings = await _settings.FirstOrDefaultAsync(new ClinicAppointmentSettingsByClinicSpec(appointment.TenantId, appointment.ClinicId), ct);
        var offers = await _offers.ListAsync(new WaitlistOffersBySourceSpec(appointment.TenantId, appointment.Id), ct);
        var isReleased = WaitlistOfferService.IsReleased(appointment, utcNow);
        var isBooked = offers.Any(o => o.Status is WaitlistOfferStatus.Accepted or WaitlistOfferStatus.SlotTaken);
        var candidates = isReleased && !isBooked ? await _service.ListCandidatesAsync(appointment, utcNow, ct) : [];

        var waiting = candidates.Count == 0 ? [] : await _entries.ListAsync(new WaitingEntriesByClinicSpec(appointment.TenantId, appointment.ClinicId), ct);
        var positions = waiting.Select((e, i) => (e.Id, i + 1)).ToDictionary(x => x.Id, x => x.Item2);

        return new WaitlistSlotDto(
            appointment.Id,
            appointment.ScheduledAtUtc,
            appointment.DurationMinutes,
            appointment.VeterinarianUserId,
            isReleased,
            settings?.WaitlistAutoOfferEnabled ?? ClinicAppointmentSettings.DefaultWaitlistAutoOfferEnabled,
            candidates.Count(c => c.Blocker == WaitlistOfferBlocker.None),
            candidates.Count(c => c.Blocker != WaitlistOfferBlocker.None),
            offers.FirstOrDefault(o => o.IsOpen) is { } open ? WaitlistAccess.ToDto(open) : null,
            isBooked,
            candidates.Select(c => new WaitlistCandidateDto(
                c.Entry.Id, positions.GetValueOrDefault(c.Entry.Id), c.Pet.Name, c.Client.FullName, c.Blocker)).ToList());
    }
}
