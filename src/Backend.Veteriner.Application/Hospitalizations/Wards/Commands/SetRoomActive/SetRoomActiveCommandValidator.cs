using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetRoomActive;

public sealed class SetRoomActiveCommandValidator : AbstractValidator<SetRoomActiveCommand>
{
    public SetRoomActiveCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.WardId).NotEmpty();
        RuleFor(x => x.RoomId).NotEmpty();
    }
}
