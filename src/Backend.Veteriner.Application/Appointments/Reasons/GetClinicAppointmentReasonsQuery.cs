using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Reasons;

/// <summary>Kliniğin randevu nedenleri (aktif/pasif dahil; sıra, sonra ad). Aktif süzmesi istemcide.</summary>
public sealed record GetClinicAppointmentReasonsQuery(Guid ClinicId) : IRequest<Result<IReadOnlyList<AppointmentReasonDto>>>;

public sealed class GetClinicAppointmentReasonsQueryValidator : AbstractValidator<GetClinicAppointmentReasonsQuery>
{
    public GetClinicAppointmentReasonsQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class GetClinicAppointmentReasonsQueryHandler
    : IRequestHandler<GetClinicAppointmentReasonsQuery, Result<IReadOnlyList<AppointmentReasonDto>>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<AppointmentReason> _reasons;

    public GetClinicAppointmentReasonsQueryHandler(ClinicDefinitionAccess access, IReadRepository<AppointmentReason> reasons)
    {
        _access = access;
        _reasons = reasons;
    }

    public async ValueTask<Result<IReadOnlyList<AppointmentReasonDto>>> Handle(
        GetClinicAppointmentReasonsQuery request,
        CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<AppointmentReasonDto>>.Failure(access.Error);

        var rows = await _reasons.ListAsync(new AppointmentReasonsByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<IReadOnlyList<AppointmentReasonDto>>.Success(rows.Select(AppointmentReasonMapping.ToDto).ToList());
    }
}
