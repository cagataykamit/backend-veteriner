using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.Create;

/// <param name="PreferredContactChannel">Verilmezse tercihsiz (<see cref="ContactChannel.None"/>).</param>
/// <param name="RemindersOptedOut">Verilmezse false (hatırlatma gönderilir).</param>
/// <param name="InvoicingIdentity">Fatura kimliği (ADR-036); verilmezse boş. Denetim kaydında TCKN/VKN maskelenir.</param>
public sealed record CreateClientCommand(
    string FullName,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ContactChannel? PreferredContactChannel = null,
    bool? RemindersOptedOut = null,
    ClientInvoicingIdentityDto? InvoicingIdentity = null)
    : IRequest<Result<ClientCreatedDto>>, IAuditableRequest
{
    public string AuditAction => "Client.Create";
    public string? AuditTarget => null;
}
