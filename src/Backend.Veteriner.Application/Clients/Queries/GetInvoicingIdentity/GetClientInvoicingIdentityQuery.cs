using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clients.Queries.GetInvoicingIdentity;

/// <summary>
/// GET /clients/{id}/invoicing-identity: düzenleme formu için tam TCKN/VKN (ADR-036). Uç <c>Clients.Update</c> ister;
/// her okuma denetim kaydına yazılır (istek kaydında numara yoktur).
/// </summary>
public sealed record GetClientInvoicingIdentityQuery(Guid ClientId)
    : IRequest<Result<ClientInvoicingIdentityDto>>, IAuditableRequest
{
    public string AuditAction => "Client.InvoicingIdentity.Read";
    public string? AuditTarget => $"ClientId={ClientId}";
}
