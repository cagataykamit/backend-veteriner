using Backend.Veteriner.Application.Clients.Queries.GetRecentSummary;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetRecentSummary.Validators;

public sealed class GetClientRecentSummaryQueryValidator : AbstractValidator<GetClientRecentSummaryQuery>
{
    public GetClientRecentSummaryQueryValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
    }
}
