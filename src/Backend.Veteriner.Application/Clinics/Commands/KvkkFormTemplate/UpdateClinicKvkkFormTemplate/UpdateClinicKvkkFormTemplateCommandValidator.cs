using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.KvkkFormTemplate.UpdateClinicKvkkFormTemplate;

/// <summary>Metin kuralı (boş/uzunluk) domain'de <c>ClinicKvkkFormTemplate</c> içindedir.</summary>
public sealed class UpdateClinicKvkkFormTemplateCommandValidator : AbstractValidator<UpdateClinicKvkkFormTemplateCommand>
{
    public UpdateClinicKvkkFormTemplateCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
