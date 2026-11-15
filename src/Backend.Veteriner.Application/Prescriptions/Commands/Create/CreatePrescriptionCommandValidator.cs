using Backend.Veteriner.Application.Prescriptions.Items;
using Backend.Veteriner.Domain.Prescriptions;
using FluentValidation;

namespace Backend.Veteriner.Application.Prescriptions.Commands.Create;

public sealed class CreatePrescriptionCommandValidator : AbstractValidator<CreatePrescriptionCommand>
{
    public CreatePrescriptionCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.PetId).NotEmpty();
        RuleFor(x => x.ExaminationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir muayene seçin.");
        RuleFor(x => x.TreatmentId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Geçerli bir tedavi seçin.");
        RuleFor(x => x.PrescribedAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => !string.IsNullOrEmpty(x.Notes));

        RuleFor(x => x.Items)
            .Must(items => items!.Count <= PrescriptionItem.MaxPerPrescription)
            .WithMessage($"Bir reçetede en fazla {PrescriptionItem.MaxPerPrescription} kalem olabilir.")
            .When(x => x.Items is not null);
        RuleForEach(x => x.Items).NotNull().SetValidator(new PrescriptionItemRequestValidator())
            .When(x => x.Items is not null);
    }
}
