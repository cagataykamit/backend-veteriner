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

        RuleFor(x => x.Rows)
            .Must(r => string.Equals(r, GetImportBatchErrorsQuery.ErrorRows, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(r, GetImportBatchErrorsQuery.SkippedRows, StringComparison.OrdinalIgnoreCase))
            .When(x => x.Rows is not null)
            .WithMessage("rows errors veya skipped olmalıdır.");

        // Katalog dışı kod LIKE koşuluna girmez.
        RuleFor(x => x.Code)
            .Must(c => Validation.ImportRules.Find(c) is not null)
            .When(x => x.Code is not null)
            .WithMessage("code bilinen bir kural kodu olmalıdır.");
    }
}
