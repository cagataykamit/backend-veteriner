using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using MediatR;

namespace Backend.Veteriner.Application.Imports.Commands.ReleaseReminders;

/// <summary>
/// POST /api/v1/imports/batches/{id}/release-reminders (ADR-019 K3, IMPORT_API_CONTRACT.md §7.10): yönetici, partinin
/// içe aktarılan aşılarını hatırlatma adayı yapar. Tek yönlü; <see cref="ConfirmEmailCount"/> onay anındaki
/// hatırlatma penceresi e-posta sayısıyla eşleşmelidir. Audit payload'ı yalnız kimliği ve sayıyı taşır.
/// </summary>
public sealed record ReleaseImportRemindersCommand(Guid Id, int? ConfirmEmailCount)
    : IRequest<Result<ImportReleaseRemindersResultDto>>, IAuditableRequest
{
    public string AuditAction => "ImportBatch.ReleaseReminders";
    public string? AuditTarget => $"ImportBatchId={Id}";
}

public sealed class ReleaseImportRemindersCommandValidator : AbstractValidator<ReleaseImportRemindersCommand>
{
    public ReleaseImportRemindersCommandValidator()
    {
        RuleFor(x => x.ConfirmEmailCount)
            .NotNull()
            .WithMessage("confirmEmailCount zorunludur.");
        RuleFor(x => x.ConfirmEmailCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("confirmEmailCount 0 veya daha büyük olmalıdır.");
    }
}
