using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Resources;

/// <summary>Kliniğin oda ve cihazları (aktif/pasif dahil; odalar, cihazlar, sıra, ad). Aktif süzmesi istemcide.</summary>
public sealed record GetClinicAppointmentResourcesQuery(Guid ClinicId) : IRequest<Result<IReadOnlyList<AppointmentResourceDto>>>;

public sealed class GetClinicAppointmentResourcesQueryValidator : AbstractValidator<GetClinicAppointmentResourcesQuery>
{
    public GetClinicAppointmentResourcesQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class GetClinicAppointmentResourcesQueryHandler
    : IRequestHandler<GetClinicAppointmentResourcesQuery, Result<IReadOnlyList<AppointmentResourceDto>>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentResource> _resources;

    public GetClinicAppointmentResourcesQueryHandler(ClinicDefinitionAccess access, IReadRepository<AppointmentResource> resources)
    {
        _access = access;
        _resources = resources;
    }

    public async ValueTask<Result<IReadOnlyList<AppointmentResourceDto>>> Handle(
        GetClinicAppointmentResourcesQuery request,
        CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<AppointmentResourceDto>>.Failure(access.Error);

        var rows = await _resources.ListAsync(new AppointmentResourcesByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<IReadOnlyList<AppointmentResourceDto>>.Success(rows.Select(AppointmentResourceMapping.ToDto).ToList());
    }
}
