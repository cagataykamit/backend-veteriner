using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Commands.UpdateCode;

/// <summary>Kodun yüzde, geçerlilik ve limitini değiştirir; kod metni değişmez, var olan kullanımlar eski yüzdeleri korur.</summary>
public sealed record UpdateDiscountCodeCommand(
    Guid SalesPartnerId,
    Guid CodeId,
    decimal DiscountPercent,
    decimal CommissionPercent,
    DateTime? ValidFromUtc,
    DateTime? ValidUntilUtc,
    int? MaxRedemptions)
    : IRequest<Result<DiscountCodeDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "DiscountCode.Update";
    public string? AuditTarget => $"DiscountCodeId={CodeId}";
}

public sealed class UpdateDiscountCodeCommandValidator : AbstractValidator<UpdateDiscountCodeCommand>
{
    public UpdateDiscountCodeCommandValidator()
    {
        RuleFor(x => x.SalesPartnerId).NotEmpty();
        RuleFor(x => x.CodeId).NotEmpty();
    }
}
