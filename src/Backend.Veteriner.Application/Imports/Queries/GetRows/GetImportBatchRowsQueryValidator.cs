using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Queries.GetRows;

public sealed class GetImportBatchRowsQueryValidator : AbstractValidator<GetImportBatchRowsQuery>
{
    public GetImportBatchRowsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetImportBatchRowsQuery.MaxPageSize);

        RuleFor(x => x.Table)
            .Must(t => ImportCanonicalNames.TryParseKey(t, out _))
            .When(x => x.Table is not null)
            .WithMessage("table customers, pets veya vaccinations olmalıdır.");

        RuleFor(x => x.Status)
            .Must(s => ImportRowStatusFilter.TryParse(s, out _, out _))
            .When(x => x.Status is not null)
            .WithMessage("status Valid, Warning, Error, Created, Matched, Skipped, Failed, RolledBack veya Kept olmalıdır.");

        // Katalog dışı kod LIKE koşuluna girmez.
        RuleFor(x => x.Code)
            .Must(c => ImportRules.Find(c) is not null)
            .When(x => x.Code is not null)
            .WithMessage("code bilinen bir kural kodu olmalıdır.");
    }
}

/// <summary>Satır listesi <c>status</c> süzgeci: doğrulama veya uygulama durumu (tek yer).</summary>
public static class ImportRowStatusFilter
{
    public static bool TryParse(
        string? value,
        out ImportRowValidationStatus? validation,
        out ImportRowApplyStatus? apply)
    {
        validation = null;
        apply = null;
        if (string.IsNullOrEmpty(value) || !char.IsLetter(value[0]))
            return false;

        if (Enum.TryParse<ImportRowValidationStatus>(value, ignoreCase: true, out var v)
            && v != ImportRowValidationStatus.Pending && Enum.IsDefined(v))
        {
            validation = v;
            return true;
        }

        if (Enum.TryParse<ImportRowApplyStatus>(value, ignoreCase: true, out var a)
            && a != ImportRowApplyStatus.Pending && Enum.IsDefined(a))
        {
            apply = a;
            return true;
        }

        return false;
    }
}
