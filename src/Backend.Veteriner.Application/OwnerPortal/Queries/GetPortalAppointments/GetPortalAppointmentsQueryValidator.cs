using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalAppointments;

public sealed class GetPortalAppointmentsQueryValidator : AbstractValidator<GetPortalAppointmentsQuery>
{
    public GetPortalAppointmentsQueryValidator()
    {
        RuleFor(x => x.Scope)
            .Must(PortalAppointmentScope.IsValid)
            .WithMessage($"scope '{PortalAppointmentScope.Upcoming}' veya '{PortalAppointmentScope.Past}' olmalıdır.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, OwnerPortalPaging.MaxPageSize);
    }
}
