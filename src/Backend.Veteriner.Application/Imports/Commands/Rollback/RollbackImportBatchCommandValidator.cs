using Backend.Veteriner.Application.Imports.Rollback;
using FluentValidation;

namespace Backend.Veteriner.Application.Imports.Commands.Rollback;

public sealed class RollbackImportBatchCommandValidator : AbstractValidator<RollbackImportBatchCommand>
{
    public RollbackImportBatchCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty().WithMessage("requestId zorunludur.");
        RuleFor(x => x.Reason)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Geri alma gerekçesi (reason) zorunludur.")
            .Must(r => r is null || r.Trim().Length <= RollbackImportBatchCommand.MaxReasonLength)
            .WithMessage($"Gerekçe en fazla {RollbackImportBatchCommand.MaxReasonLength} karakter olabilir.");
        RuleFor(x => x.BlockedPolicy)
            .Must(p => p is null || ImportRollbackPolicy.All.Contains(p))
            .WithMessage($"blockedPolicy yalnız {string.Join(", ", ImportRollbackPolicy.All)} olabilir.");
    }
}
