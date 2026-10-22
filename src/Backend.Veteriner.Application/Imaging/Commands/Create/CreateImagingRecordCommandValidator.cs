using Backend.Veteriner.Domain.Imaging;
using FluentValidation;

namespace Backend.Veteriner.Application.Imaging.Commands.Create;

public sealed class CreateImagingRecordCommandValidator : AbstractValidator<CreateImagingRecordCommand>
{
    public CreateImagingRecordCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ExaminationId is invalid.");
        RuleFor(x => x.PerformedAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Modality).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(ImagingRecord.DescriptionMaxLength);
        RuleFor(x => x.ClinicalNote).MaximumLength(ImagingRecord.ClinicalNoteMaxLength);
    }
}
