using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Queries.GetDataExport;

/// <summary>
/// Müşteri veri kopyası (ADR-032 madde 2, KVKK bilgi talebi). Salt okunur; erişim audit'e yazılır
/// (kim, hangi müşteri, ne zaman). Audit gövdesi yalnız bu sorgudur (müşteri kimliği); dışa verilen veri yazılmaz.
/// </summary>
public sealed record GetClientDataExportQuery(Guid ClientId)
    : IRequest<Result<ClientDataExportDto>>, IAuditableRequest
{
    public string AuditAction => "Client.DataExport";
    public string? AuditTarget => $"ClientId={ClientId}";
}
