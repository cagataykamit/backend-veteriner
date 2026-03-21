using Backend.Veteriner.Application.Appointments.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Appointments.Queries.GetById.Validators;

public sealed class GetAppointmentByIdQueryValidator : AbstractValidator<GetAppointmentByIdQuery>
{
    public GetAppointmentByIdQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}
