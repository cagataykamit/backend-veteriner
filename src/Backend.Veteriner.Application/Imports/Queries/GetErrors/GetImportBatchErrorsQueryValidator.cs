using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Queries.GetErrors;

public sealed class GetImportBatchErrorsQueryValidator : AbstractValidator<GetImportBatchErrorsQuery>
{
    public GetImportBatchErrorsQueryValidator()
    {
        RuleFor(x => x.Format)
            .Must(f => f is GetImportBatchErrorsQuery.Xlsx or GetImportBatchErrorsQuery.Csv)
            .When(x => x.Format is not null)
            .WithMessage("format xlsx veya csv olmalıdır.");
    }
}
