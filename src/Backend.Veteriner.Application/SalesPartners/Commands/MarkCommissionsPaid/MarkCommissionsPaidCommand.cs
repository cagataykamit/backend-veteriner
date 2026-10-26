using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.MarkCommissionsPaid;

/// <summary>
/// Komisyon ödemesi sistem dışıdır (ADR-037 madde 8); yönetici ödenen satırları tarih ve notla işaretler. Satırların hepsi ortağın
/// ve tahakkuk durumunda olmalı; biri uymazsa hiçbiri değişmez.
/// </summary>
public sealed record MarkCommissionsPaidCommand(Guid SalesPartnerId, IReadOnlyList<Guid> CollectionIds, DateTime PaidAtUtc, string? Note)
    : IRequest<Result<MarkCommissionsPaidResultDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "DiscountCommission.MarkPaid";
    public string? AuditTarget => $"SalesPartnerId={SalesPartnerId}";
}

public sealed class MarkCommissionsPaidCommandValidator : AbstractValidator<MarkCommissionsPaidCommand>
{
    public const int MaxCollections = 500;

    public MarkCommissionsPaidCommandValidator()
    {
        RuleFor(x => x.SalesPartnerId).NotEmpty();
        RuleFor(x => x.CollectionIds).NotEmpty().Must(ids => ids.Count <= MaxCollections)
            .WithMessage($"Tek seferde en çok {MaxCollections} satır işaretlenebilir.");
        RuleForEach(x => x.CollectionIds).NotEmpty();
        RuleFor(x => x.PaidAtUtc).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(DiscountRedemptionCollection.MaxNoteLength);
    }
}
