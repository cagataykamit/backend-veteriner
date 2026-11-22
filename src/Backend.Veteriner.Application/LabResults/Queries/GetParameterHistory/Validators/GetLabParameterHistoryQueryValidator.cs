using Backend.Veteriner.Domain.LabResults;
using FluentValidation;

namespace Backend.Veteriner.Application.LabResults.Queries.GetParameterHistory.Validators;

public sealed class GetLabParameterHistoryQueryValidator : AbstractValidator<GetLabParameterHistoryQuery>
{
    public GetLabParameterHistoryQueryValidator()
    {
        RuleFor(x => x.PetId).NotEmpty().WithMessage("Hasta seçilmelidir.");
        RuleFor(x => x.Parameter)
            .NotEmpty().WithMessage("Parametre adı zorunludur.")
            .MaximumLength(LabResultLine.ParameterNameMaxLength);
        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("Klinik filtresi geçersiz.");
    }
}
