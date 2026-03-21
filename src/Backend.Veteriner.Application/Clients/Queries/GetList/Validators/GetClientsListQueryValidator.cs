using Backend.Veteriner.Application.Clients.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetList.Validators;

public sealed class GetClientsListQueryValidator : AbstractValidator<GetClientsListQuery>
{
    public GetClientsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
    }
}
