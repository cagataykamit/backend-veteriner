using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetBalance.Validators;

public sealed class GetClientBalanceQueryValidator : AbstractValidator<GetClientBalanceQuery>
{
    public GetClientBalanceQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
