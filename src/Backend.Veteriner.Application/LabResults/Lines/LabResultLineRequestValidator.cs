using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.LabResults;
using FluentValidation;

namespace Backend.Veteriner.Application.LabResults.Lines;

/// <summary>Biçim denetimi; sayı ve referans aralığı kuralları alandadır (<see cref="LabResult.ValidateResult(DateTime?, string?, string?, IReadOnlyList{LabResultLineDraft}?)"/>).</summary>
public sealed class LabResultLineRequestValidator : AbstractValidator<LabResultLineRequest>
{
    public LabResultLineRequestValidator()
    {
        RuleFor(x => x.ParameterName)
            .NotEmpty().WithMessage("Parametre adı zorunludur.")
            .MaximumLength(LabResultLine.ParameterNameMaxLength);
        RuleFor(x => x.Value).NotNull().WithMessage("Değer zorunludur.");
        RuleFor(x => x.Unit).MaximumLength(LabResultLine.UnitMaxLength);
    }
}
