using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Commands.ApplySubscriptionCode;

/// <summary>
/// Kayıttan sonra, ilk ücretli tahsilattan önce kiracının ortak kodu girmesi (ADR-037 madde 5). Süresi dolmuş denemede
/// (salt okunur) ödeme öncesi kod girilebilsin diye abonelik yazma korumasından muaftır.
/// </summary>
public sealed record ApplySubscriptionDiscountCodeCommand(Guid TenantId, string Code)
    : IRequest<Result<SubscriptionDiscountDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "DiscountCode.ApplyToSubscription";
    public string? AuditTarget => $"TenantId={TenantId}";
}

public sealed class ApplySubscriptionDiscountCodeCommandValidator : AbstractValidator<ApplySubscriptionDiscountCodeCommand>
{
    public ApplySubscriptionDiscountCodeCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
    }
}
