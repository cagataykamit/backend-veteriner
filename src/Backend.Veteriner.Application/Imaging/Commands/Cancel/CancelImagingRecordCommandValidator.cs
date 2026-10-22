using FluentValidation;

namespace Backend.Veteriner.Application.Imaging.Commands.Cancel;

public sealed class CancelImagingRecordCommandValidator : AbstractValidator<CancelImagingRecordCommand>
{
    public CancelImagingRecordCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
