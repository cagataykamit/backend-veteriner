using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Queries.GetList;

public sealed class GetImportBatchesListQueryValidator : AbstractValidator<GetImportBatchesListQuery>
{
    public GetImportBatchesListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
    }
}
