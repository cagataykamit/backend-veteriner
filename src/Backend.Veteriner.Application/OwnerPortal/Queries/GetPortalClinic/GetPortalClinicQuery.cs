using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalClinic;

/// <summary>Bağın kliniğinin iletişim bilgisi ve haftalık çalışma saatleri (docs/PORTAL_API_CONTRACT.md §6.1).</summary>
public sealed record GetPortalClinicQuery(Guid OwnerAccountId, Guid LinkId) : IRequest<Result<PortalClinicDto>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Clinic";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class GetPortalClinicQueryHandler : IRequestHandler<GetPortalClinicQuery, Result<PortalClinicDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;

    public GetPortalClinicQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> workingHours)
    {
        _access = access;
        _clinics = clinics;
        _workingHours = workingHours;
    }

    public async Task<Result<PortalClinicDto>> Handle(GetPortalClinicQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PortalClinicDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(scope.TenantId, scope.ClinicId), ct);
        if (clinic is null)
            return Result<PortalClinicDto>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        var hours = await _workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(scope.TenantId, scope.ClinicId), ct);

        return Result<PortalClinicDto>.Success(new PortalClinicDto(
            clinic.Id,
            clinic.Name,
            clinic.City,
            clinic.Phone,
            clinic.Email,
            clinic.Address,
            hours
                .OrderBy(h => MondayFirst(h.DayOfWeek))
                .Select(h => new PortalClinicWorkingHourDto(h.DayOfWeek, h.IsClosed, h.OpensAt, h.ClosesAt, h.BreakStartsAt, h.BreakEndsAt))
                .ToArray()));
    }

    /// <summary>Hafta Pazartesi başlar (Türkiye).</summary>
    private static int MondayFirst(DayOfWeek day) => ((int)day + 6) % 7;
}
