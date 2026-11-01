using Backend.Veteriner.Domain.Visits;
using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.SetPreparation;

public sealed class SetVisitPreparationCommandValidator : AbstractValidator<SetVisitPreparationCommand>
{
    public SetVisitPreparationCommandValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
        RuleFor(x => x.ArrivalReason).MaximumLength(Visit.MaxArrivalReasonLength);
        RuleFor(x => x.PreparedExaminationTemplateId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("PreparedExaminationTemplateId gecersiz.");
    }
}
