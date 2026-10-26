using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CreatePartner;

/// <summary>Platform yöneticisi satış ortağı ekler (aktif doğar). Kiracı verisi olmadığından abonelik yazma korumasından muaf.</summary>
public sealed record CreateSalesPartnerCommand(string Name, string? Email, string? Phone, Guid? LinkedTenantId)
    : IRequest<Result<SalesPartnerDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "SalesPartner.Create";
    public string? AuditTarget => null;
}
