using Backend.Veteriner.Application.Imports.Mapping;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Queries.GetTemplate;

public sealed class GetImportTemplateQueryValidator : AbstractValidator<GetImportTemplateQuery>
{
    public const string Xlsx = "xlsx";
    public const string Csv = "csv";

    public GetImportTemplateQueryValidator()
    {
        RuleFor(x => x.Format)
            .Must(f => f is Xlsx or Csv)
            .WithMessage("format xlsx veya csv olmalıdır.");
        RuleFor(x => x.Table)
            .Must(t => ImportCanonicalNames.TryParseKey(t, out _))
            .When(x => x.Format == Csv)
            .WithMessage("format=csv için table customers, pets veya vaccinations olmalıdır.");
    }
}
