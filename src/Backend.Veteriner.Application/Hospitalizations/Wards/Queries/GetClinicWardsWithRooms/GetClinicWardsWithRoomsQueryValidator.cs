using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Queries.GetClinicWardsWithRooms;

public sealed class GetClinicWardsWithRoomsQueryValidator : AbstractValidator<GetClinicWardsWithRoomsQuery>
{
    public GetClinicWardsWithRoomsQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
