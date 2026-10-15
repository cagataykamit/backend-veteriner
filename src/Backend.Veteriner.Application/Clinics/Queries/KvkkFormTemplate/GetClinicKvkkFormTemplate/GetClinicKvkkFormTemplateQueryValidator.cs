using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.KvkkFormTemplate.GetClinicKvkkFormTemplate;

public sealed class GetClinicKvkkFormTemplateQueryValidator : AbstractValidator<GetClinicKvkkFormTemplateQuery>
{
    public GetClinicKvkkFormTemplateQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
    }
}
