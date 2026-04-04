using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetPaymentSummary.Validators;

public sealed class GetClientPaymentSummaryQueryValidator : AbstractValidator<GetClientPaymentSummaryQuery>
{
    public GetClientPaymentSummaryQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
