using Backend.Veteriner.Application.Clients.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetList.Validators;

public sealed class GetClientsListQueryValidator : AbstractValidator<GetClientsListQuery>
{
    public GetClientsListQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
