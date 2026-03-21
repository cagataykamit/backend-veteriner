using Backend.Veteriner.Application.Clients.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Clients.Queries.GetById.Validators;

public sealed class GetClientByIdQueryValidator : AbstractValidator<GetClientByIdQuery>
{
    public GetClientByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
