using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalAppointments;

/// <summary>
/// Müşterinin hayvanlarının randevuları (docs/PORTAL_API_CONTRACT.md §6.4): <c>upcoming</c> istek anından sonra artan,
/// <c>past</c> son 12 ay azalan. Tüm durumlar (iptal dahil) döner; not dönmez.
/// </summary>
public sealed record GetPortalAppointmentsQuery(Guid OwnerAccountId, Guid LinkId, string Scope, int Page, int PageSize)
    : IRequest<Result<PagedResult<PortalAppointmentDto>>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Appointments";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

/// <summary>Randevu kapsamı ve geçmiş penceresi (tek yer).</summary>
public static class PortalAppointmentScope
{
    public const string Upcoming = "upcoming";
    public const string Past = "past";
    public const int PastWindowMonths = 12;

    public static bool IsValid(string? scope)
        => string.Equals(scope, Upcoming, StringComparison.OrdinalIgnoreCase)
           || string.Equals(scope, Past, StringComparison.OrdinalIgnoreCase);

    public static bool IsPast(string scope) => string.Equals(scope, Past, StringComparison.OrdinalIgnoreCase);
}

public sealed class GetPortalAppointmentsQueryHandler
    : IRequestHandler<GetPortalAppointmentsQuery, Result<PagedResult<PortalAppointmentDto>>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly TimeProvider _timeProvider;

    public GetPortalAppointmentsQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Clinic> clinics,
        TimeProvider timeProvider)
    {
        _access = access;
        _pets = pets;
        _appointments = appointments;
        _clinics = clinics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<PagedResult<PortalAppointmentDto>>> Handle(GetPortalAppointmentsQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<PortalAppointmentDto>>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var pets = await _pets.ListAsync(new PortalPetNamesByClientSpec(scope.TenantId, scope.ClientId), ct);
        if (pets.Count == 0)
            return Result<PagedResult<PortalAppointmentDto>>.Success(PagedResult<PortalAppointmentDto>.Create([], 0, request.Page, request.PageSize));

        var petIds = pets.Select(p => p.Id).ToArray();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var past = PortalAppointmentScope.IsPast(request.Scope);
        var (fromUtc, toUtcExclusive) = past
            ? (nowUtc.AddMonths(-PortalAppointmentScope.PastWindowMonths), (DateTime?)nowUtc)
            : (nowUtc, null);

        var total = await _appointments.CountAsync(
            new PortalAppointmentsSpec(scope.TenantId, petIds, fromUtc, toUtcExclusive, ascending: !past), ct);
        var rows = total == 0
            ? []
            : await _appointments.ListAsync(
                new PortalAppointmentsSpec(scope.TenantId, petIds, fromUtc, toUtcExclusive, ascending: !past, request.Page, request.PageSize), ct);

        var petNames = pets.ToDictionary(p => p.Id, p => p.Name);
        var clinicIds = rows.Select(r => r.ClinicId).Distinct().ToArray();
        var clinicNames = clinicIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clinics.ListAsync(new PortalClinicsByIdsSpec(clinicIds), ct))
                .Where(c => c.TenantId == scope.TenantId)
                .ToDictionary(c => c.Id, c => c.Name);

        var items = rows
            .Select(r => new PortalAppointmentDto(
                r.Id,
                r.PetId,
                petNames.GetValueOrDefault(r.PetId, string.Empty),
                r.ClinicId,
                clinicNames.GetValueOrDefault(r.ClinicId),
                r.ScheduledAtUtc,
                r.DurationMinutes,
                r.AppointmentType,
                r.Status))
            .ToArray();

        return Result<PagedResult<PortalAppointmentDto>>.Success(
            PagedResult<PortalAppointmentDto>.Create(items, total, request.Page, request.PageSize));
    }
}
