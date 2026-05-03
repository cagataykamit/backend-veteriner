using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos.Validators;
using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours.Validators;

public sealed class UpdateClinicWorkingHoursCommandValidator : AbstractValidator<UpdateClinicWorkingHoursCommand>
{
    public UpdateClinicWorkingHoursCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();

        RuleFor(x => x.Items)
            .NotNull()
            .Must(i => i.Count == 7)
            .WithMessage("Tam 7 gün için çalışma saati satırı gerekir.");

        RuleFor(x => x.Items)
            .Must(HaveSevenDistinctDays)
            .When(x => x.Items is not null && x.Items.Count == 7)
            .WithMessage("Her gün tam bir kez verilmelidir (yinelenen veya eksik gün yok).");

        RuleForEach(x => x.Items)
            .SetValidator(new ClinicWorkingHourDtoValidator())
            .When(x => x.Items is not null);
    }

    private static bool HaveSevenDistinctDays(IReadOnlyList<ClinicWorkingHourDto>? items)
    {
        if (items is null || items.Count != 7)
            return false;
        return items.Select(i => i.DayOfWeek).Distinct().Count() == 7;
    }
}
