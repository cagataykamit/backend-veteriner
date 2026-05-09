using FluentValidation;

namespace Backend.Veteriner.Application.Products.Queries.GetById.Validators;

public sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
