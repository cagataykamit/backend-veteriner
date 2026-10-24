using Backend.Veteriner.Domain.Hospitalizations;
using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateRoom;

public sealed class UpdateRoomCommandValidator : AbstractValidator<UpdateRoomCommand>
{
    public UpdateRoomCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.WardId).NotEmpty();
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(HospitalizationRoom.NameMaxLength);
    }
}
