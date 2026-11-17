using Backend.Veteriner.Application.Imports.Mapping;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Commands.Commit;

public sealed class CommitImportBatchCommandValidator : AbstractValidator<CommitImportBatchCommand>
{
    public CommitImportBatchCommandValidator()
    {
        RuleFor(x => x.ValidationId).NotEmpty().WithMessage("validationId zorunludur.");
        RuleFor(x => x.OnErrors)
            .Must(v => v is null || CommitImportBatchCommand.AllowedOnErrors.Contains(v))
            .WithMessage($"onErrors yalnız {string.Join(", ", CommitImportBatchCommand.AllowedOnErrors)} olabilir.");
        RuleFor(x => x.BalanceDifferences)
            .Must(v => v is null || ImportOptionsDocument.AllowedBalanceDifferenceModes.Contains(v))
            .WithMessage($"balanceDifferences yalnız {string.Join(", ", ImportOptionsDocument.AllowedBalanceDifferenceModes)} olabilir.");
    }
}
