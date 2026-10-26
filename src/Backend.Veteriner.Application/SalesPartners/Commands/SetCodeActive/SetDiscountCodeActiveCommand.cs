using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.SetCodeActive;

/// <summary>Kodu kapatır ya da yeniden açar; kapalı kod yeni kullanımda reddedilir, var olan kullanımlar sürer.</summary>
public sealed record SetDiscountCodeActiveCommand(Guid SalesPartnerId, Guid CodeId, bool IsActive)
    : IRequest<Result<DiscountCodeDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => IsActive ? "DiscountCode.Activate" : "DiscountCode.Deactivate";
    public string? AuditTarget => $"DiscountCodeId={CodeId}";
}
