using Backend.Veteriner.Domain.Hospitalizations;
using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateRoom;

public sealed class CreateRoomCommandValidator : AbstractValidator<CreateRoomCommand>
{
    public CreateRoomCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.WardId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalizationRoom.NameMaxLength);
    }
}
