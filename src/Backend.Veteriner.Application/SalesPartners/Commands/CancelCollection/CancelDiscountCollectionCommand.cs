using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CancelCollection;

/// <summary>
/// Tahsilat iade/iptal edildi (ADR-037 madde 4): komisyon geri alınır. Ödeme sağlayıcısı iade olayı bildirmediği için elle işaretlenir.
/// </summary>
public sealed record CancelDiscountCollectionCommand(Guid SalesPartnerId, Guid CollectionId, string Reason)
    : IRequest<Result<DiscountCollectionDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "DiscountCollection.Cancel";
    public string? AuditTarget => $"CollectionId={CollectionId}";
}

public sealed class CancelDiscountCollectionCommandValidator : AbstractValidator<CancelDiscountCollectionCommand>
{
    public CancelDiscountCollectionCommandValidator()
    {
        RuleFor(x => x.SalesPartnerId).NotEmpty();
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(DiscountRedemptionCollection.MaxNoteLength);
    }
}
