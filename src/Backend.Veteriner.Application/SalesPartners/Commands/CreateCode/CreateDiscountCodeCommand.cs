using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CreateCode;

/// <summary>Ortağa indirim kodu ekler; yüzdeler kod başına zorunlu (ADR-037 madde 2). Biçim/aralık kuralı domain'dedir.</summary>
public sealed record CreateDiscountCodeCommand(
    Guid SalesPartnerId,
    string Code,
    decimal DiscountPercent,
    decimal CommissionPercent,
    DateTime? ValidFromUtc,
    DateTime? ValidUntilUtc,
    int? MaxRedemptions)
    : IRequest<Result<DiscountCodeDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "DiscountCode.Create";
    public string? AuditTarget => $"SalesPartnerId={SalesPartnerId}";
}

public sealed class CreateDiscountCodeCommandValidator : AbstractValidator<CreateDiscountCodeCommand>
{
    public CreateDiscountCodeCommandValidator()
    {
        RuleFor(x => x.SalesPartnerId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
    }
}
