using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.AppointmentRequests.Portal;

/// <summary>Bağın kliniğinin talep sayfası (online randevu kapalıysa 404 <c>OnlineBooking.NotFound</c>).</summary>
public sealed record GetPortalBookingQuery(Guid OwnerAccountId, Guid LinkId) : IRequest<Result<OnlineBookingPageDto>>;

public sealed record GetPortalBookingAvailabilityQuery(Guid OwnerAccountId, Guid LinkId, Guid? ReasonId, DateOnly? From, int Days)
    : IRequest<Result<IReadOnlyList<OnlineAvailabilityDayDto>>>;

/// <summary>
/// Portal talebi (ADR-042 K1; APPT-003/004): sahip bağın müşterisinin hayvanını seçer ya da serbest metin hayvan yazar. Portal
/// klinik kaydını değiştirmez (ADR-031); serbest metin hayvan onayda klinikçe oluşturulur. Neden boşsa "Diğer / emin değilim":
/// not zorunlu, kural domain'de (<see cref="AppointmentRequest.NoteRequiredCode"/>).
/// </summary>
public sealed record CreatePortalAppointmentRequestCommand(
    Guid OwnerAccountId,
    Guid LinkId,
    Guid? PetId,
    string? PetName,
    string? PetSpecies,
    string? PetBreed,
    Guid? ReasonId,
    DateTime RequestedStartUtc,
    string? Phone,
    string? Note)
    : IRequest<Result<AppointmentRequestCreatedDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.Create.Portal";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

/// <summary>Bağın müşterisinin bu klinikteki talepleri (son 50, yeniden eskiye).</summary>
public sealed record GetPortalAppointmentRequestsQuery(Guid OwnerAccountId, Guid LinkId)
    : IRequest<Result<IReadOnlyList<PortalAppointmentRequestDto>>>;

/// <summary>Sahip bekleyen talebini iptal eder; kuyruk yetkisi olanlara bildirim gider.</summary>
public sealed record CancelPortalAppointmentRequestCommand(Guid OwnerAccountId, Guid LinkId, Guid RequestId)
    : IRequest<Result<PortalAppointmentRequestDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.Cancel.Portal";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};RequestId={RequestId}";
}

public sealed class GetPortalBookingAvailabilityQueryValidator : AbstractValidator<GetPortalBookingAvailabilityQuery>
{
    public GetPortalBookingAvailabilityQueryValidator()
    {
        RuleFor(x => x.ReasonId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x.Days).InclusiveBetween(1, AppointmentAvailabilityCalculator.MaxDays);
    }
}

public sealed class CreatePortalAppointmentRequestCommandValidator : AbstractValidator<CreatePortalAppointmentRequestCommand>
{
    public CreatePortalAppointmentRequestCommandValidator()
    {
        RuleFor(x => x.LinkId).NotEmpty();
        RuleFor(x => x.ReasonId).Must(id => id is null || id != Guid.Empty);
        RuleFor(x => x).Must(x => x.PetId is not null || !string.IsNullOrWhiteSpace(x.PetName))
            .WithMessage("Hayvan seçilmeli ya da hayvan adı yazılmalıdır.");
        RuleFor(x => x.PetName).MaximumLength(AppointmentRequest.PetNameMaxLength);
        RuleFor(x => x.PetSpecies).MaximumLength(AppointmentRequest.PetSpeciesMaxLength);
        RuleFor(x => x.PetBreed).MaximumLength(AppointmentRequest.PetBreedMaxLength);
        RuleFor(x => x.Phone).MaximumLength(AppointmentRequest.PhoneMaxLength);
        RuleFor(x => x.RequestedStartUtc).Must(d => d != default).WithMessage("Talep saati zorunludur.");
        RuleFor(x => x.Note).MaximumLength(AppointmentRequest.NoteMaxLength);
    }
}

internal static class PortalAppointmentRequestMapping
{
    public static PortalAppointmentRequestDto ToDto(AppointmentRequest r, string? petName) => new(
        r.Id,
        r.Status,
        r.ReasonName,
        r.RequestedStartUtc,
        r.DurationMinutes,
        r.PetId,
        petName ?? r.PetName,
        r.ApprovedStartUtc,
        r.WasRescheduled,
        r.RejectionReason,
        r.CreatedAtUtc,
        r.IsPending);
}

public sealed class GetPortalBookingQueryHandler : IRequestHandler<GetPortalBookingQuery, Result<OnlineBookingPageDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestIntake _intake;

    public GetPortalBookingQueryHandler(OwnerPortalAccess access, OnlineBookingClinicResolver resolver, AppointmentRequestIntake intake)
    {
        _access = access;
        _resolver = resolver;
        _intake = intake;
    }

    public async ValueTask<Result<OnlineBookingPageDto>> Handle(GetPortalBookingQuery request, CancellationToken ct)
    {
        var link = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!link.IsSuccess)
            return Result<OnlineBookingPageDto>.Failure(link.Error);

        var scope = await _resolver.ResolveByClinicAsync(link.Value!.TenantId, link.Value.ClinicId, ct);
        return scope.IsSuccess
            ? Result<OnlineBookingPageDto>.Success(await _intake.GetPageAsync(scope.Value!, ct))
            : Result<OnlineBookingPageDto>.Failure(scope.Error);
    }
}

public sealed class GetPortalBookingAvailabilityQueryHandler
    : IRequestHandler<GetPortalBookingAvailabilityQuery, Result<IReadOnlyList<OnlineAvailabilityDayDto>>>
{
    private readonly OwnerPortalAccess _access;
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestIntake _intake;

    public GetPortalBookingAvailabilityQueryHandler(OwnerPortalAccess access, OnlineBookingClinicResolver resolver, AppointmentRequestIntake intake)
    {
        _access = access;
        _resolver = resolver;
        _intake = intake;
    }

    public async ValueTask<Result<IReadOnlyList<OnlineAvailabilityDayDto>>> Handle(GetPortalBookingAvailabilityQuery request, CancellationToken ct)
    {
        var link = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!link.IsSuccess)
            return Result<IReadOnlyList<OnlineAvailabilityDayDto>>.Failure(link.Error);

        var scope = await _resolver.ResolveByClinicAsync(link.Value!.TenantId, link.Value.ClinicId, ct);
        return scope.IsSuccess
            ? await _intake.GetAvailabilityAsync(scope.Value!, request.ReasonId, request.From, request.Days, ct)
            : Result<IReadOnlyList<OnlineAvailabilityDayDto>>.Failure(scope.Error);
    }
}

public sealed class CreatePortalAppointmentRequestCommandHandler
    : IRequestHandler<CreatePortalAppointmentRequestCommand, Result<AppointmentRequestCreatedDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly AppointmentRequestIntake _intake;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly TimeProvider _timeProvider;

    public CreatePortalAppointmentRequestCommandHandler(
        OwnerPortalAccess access,
        OnlineBookingClinicResolver resolver,
        AppointmentRequestIntake intake,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        TimeProvider timeProvider)
    {
        _access = access;
        _resolver = resolver;
        _intake = intake;
        _clients = clients;
        _pets = pets;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestCreatedDto>> Handle(CreatePortalAppointmentRequestCommand request, CancellationToken ct)
    {
        var account = await _access.GetActiveAccountAsync(request.OwnerAccountId, ct);
        if (!account.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(account.Error);

        var linkResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!linkResult.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(linkResult.Error);
        var link = linkResult.Value!;

        var scopeResult = await _resolver.ResolveByClinicAsync(link.TenantId, link.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        if (request.PetId is { } petId)
        {
            var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(link.TenantId, petId), ct);
            if (pet is null || pet.ClientId != link.ClientId)
                return Result<AppointmentRequestCreatedDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(link.TenantId, link.ClientId), ct);
        var email = AppointmentRequest.NormalizeEmail(account.Value!.Email);
        if (client is null || email is null)
            return Result<AppointmentRequestCreatedDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        var reason = await _intake.ResolveOnlineReasonAsync(scope, request.ReasonId, ct);
        if (!reason.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(reason.Error);

        var startUtc = AppointmentRequestIntake.NormalizeUtc(request.RequestedStartUtc);
        var bookable = await _intake.EnsureBookableAsync(scope, reason.Value, startUtc, email, ct);
        if (!bookable.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(bookable.Error);

        var created = AppointmentRequest.CreateFromPortal(
            link.TenantId,
            link.ClinicId,
            link.OwnerAccountId,
            link.ClientId,
            request.PetId,
            request.PetId is null ? new PetText(request.PetName, request.PetSpecies, request.PetBreed) : null,
            reason.Value,
            startUtc,
            client.FullName,
            email,
            string.IsNullOrWhiteSpace(request.Phone) ? client.Phone : request.Phone,
            request.Note,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<AppointmentRequestCreatedDto>.Failure(created.Error);

        return Result<AppointmentRequestCreatedDto>.Success(await _intake.SubmitAsync(created.Value!, ct));
    }
}

public sealed class GetPortalAppointmentRequestsQueryHandler
    : IRequestHandler<GetPortalAppointmentRequestsQuery, Result<IReadOnlyList<PortalAppointmentRequestDto>>>
{
    private const int Take = 50;

    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<AppointmentRequest> _requests;
    private readonly IReadRepository<Pet> _pets;

    public GetPortalAppointmentRequestsQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<AppointmentRequest> requests,
        IReadRepository<Pet> pets)
    {
        _access = access;
        _requests = requests;
        _pets = pets;
    }

    public async ValueTask<Result<IReadOnlyList<PortalAppointmentRequestDto>>> Handle(GetPortalAppointmentRequestsQuery request, CancellationToken ct)
    {
        var link = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!link.IsSuccess)
            return Result<IReadOnlyList<PortalAppointmentRequestDto>>.Failure(link.Error);
        var scope = link.Value!;

        var rows = await _requests.ListAsync(new PortalAppointmentRequestsSpec(scope.TenantId, scope.ClinicId, scope.ClientId, Take), ct);
        var petNames = (await _pets.ListAsync(new Backend.Veteriner.Application.OwnerPortal.Specs.PortalPetNamesByClientSpec(scope.TenantId, scope.ClientId), ct))
            .ToDictionary(p => p.Id, p => p.Name);

        IReadOnlyList<PortalAppointmentRequestDto> dto = rows
            .Select(r => PortalAppointmentRequestMapping.ToDto(r, r.PetId is { } id ? petNames.GetValueOrDefault(id) : null))
            .ToList();
        return Result<IReadOnlyList<PortalAppointmentRequestDto>>.Success(dto);
    }
}

public sealed class CancelPortalAppointmentRequestCommandHandler
    : IRequestHandler<CancelPortalAppointmentRequestCommand, Result<PortalAppointmentRequestDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IRepository<AppointmentRequest> _requests;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public CancelPortalAppointmentRequestCommandHandler(
        OwnerPortalAccess access,
        IRepository<AppointmentRequest> requests,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider)
    {
        _access = access;
        _requests = requests;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PortalAppointmentRequestDto>> Handle(CancelPortalAppointmentRequestCommand request, CancellationToken ct)
    {
        var link = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!link.IsSuccess)
            return Result<PortalAppointmentRequestDto>.Failure(link.Error);
        var scope = link.Value!;

        var row = await _requests.FirstOrDefaultAsync(new AppointmentRequestByIdSpec(scope.TenantId, request.RequestId), ct);
        if (row is null || row.ClinicId != scope.ClinicId || row.ClientId != scope.ClientId || row.Source != AppointmentRequestSource.Portal)
            return Result<PortalAppointmentRequestDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        var cancelled = row.CancelByOwner(_timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return Result<PortalAppointmentRequestDto>.Failure(cancelled.Error);

        await _notifications.AppointmentRequestCancelledAsync(row, ct);
        await _requests.SaveChangesAsync(ct);
        return Result<PortalAppointmentRequestDto>.Success(PortalAppointmentRequestMapping.ToDto(row, null));
    }
}
