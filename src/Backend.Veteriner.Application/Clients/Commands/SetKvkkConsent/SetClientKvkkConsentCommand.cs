using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clients.Commands.SetKvkkConsent;

/// <summary>
/// İmzalı KVKK açık rıza formunun sonucunu (Kabul/Ret) ve tarihini işaretler veya yanlış girişi "Yok"a geri alır (ADR-021).
/// <see cref="RowVersion"/> zorunludur; eski sürümle yazım 409 döner. Hatırlatma tercihine dokunmaz.
/// </summary>
public sealed record SetClientKvkkConsentCommand(
    Guid Id,
    KvkkConsentStatus Status,
    DateOnly? RecordedOn,
    string? RowVersion)
    : IRequest<Result<ClientKvkkConsentDto>>, IAuditableRequest
{
    public string AuditAction => $"Client.KvkkConsent.{Status}";
    public string? AuditTarget => $"ClientId={Id}";
}
