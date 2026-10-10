using FluentValidation;

namespace Backend.Veteriner.Application.Charges.Queries.GetList;

public sealed class GetChargesListQueryValidator : AbstractValidator<GetChargesListQuery>
{
    public GetChargesListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
